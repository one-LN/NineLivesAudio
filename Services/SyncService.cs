using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Data;
using NineLivesAudio.Helpers;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public class SyncService : ISyncService, IDisposable
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILocalDatabase _database;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly IConnectivityService _connectivity;
    private readonly IOfflineProgressQueue _offlineQueue;
    private Timer? _syncTimer;
    private bool _isSyncing;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    // Guards the library pull so the startup sync and a page load cannot download
    // the same libraries twice at the same time.
    private readonly SemaphoreSlim _librariesLock = new(1, 1);

    // Progress coordinator state
    private string? _activeItemId;
    private double _lastSyncedTime;
    private DateTime _lastSyncTimestamp = DateTime.MinValue;
    private static readonly TimeSpan MinSyncInterval = TimeSpan.FromSeconds(30);
    private const double MinPositionDelta = 2.0;    // seconds
    private const double MinProgressDelta = 0.01;   // 1%

    public bool IsSyncing => _isSyncing;
    public DateTime? LastSyncTime { get; private set; }

    public SyncService(
        IAudioBookshelfApiService apiService,
        ILocalDatabase database,
        ISettingsService settingsService,
        ILoggingService loggingService,
        IConnectivityService connectivity,
        IOfflineProgressQueue offlineQueue)
    {
        _apiService = apiService;
        _database = database;
        _settingsService = settingsService;
        _logger = loggingService;
        _connectivity = connectivity;
        _offlineQueue = offlineQueue;
    }

    public Task StartAsync()
    {
        if (_syncTimer != null) return Task.CompletedTask;

        var intervalMinutes = _settingsService.Settings.SyncIntervalMinutes;
        var interval = TimeSpan.FromMinutes(intervalMinutes > 0 ? intervalMinutes : 5);

        _syncTimer = new Timer(
            _ =>
            {
                // Timer callback is async-void by design; SyncNowAsync already
                // guards re-entry via _syncLock and catches its own exceptions.
                _ = SyncNowAsync();
            },
            null,
            TimeSpan.FromSeconds(3), // Initial delay — fast first sync so Home page gets data quickly
            interval);

        _logger.Log($"[Sync] Started with {interval.TotalMinutes}min interval");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
        _logger.Log("[Sync] Stopped");
        return Task.CompletedTask;
    }

    public async Task SyncNowAsync()
    {
        if (!_apiService.IsAuthenticated)
        {
            _logger.LogDebug("[Sync] Skipping — not authenticated");
            return;
        }

        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogDebug("[Sync] Already in progress, skipping");
            return;
        }

        try
        {
            _isSyncing = true;
            WeakReferenceMessenger.Default.Send(new SyncStartedMessage(new SyncEventArgs()));

            await SyncLibrariesAsync();
            await SyncProgressAsync();

            LastSyncTime = DateTime.Now;
            WeakReferenceMessenger.Default.Send(new SyncCompletedMessage(new SyncEventArgs()));

            _logger.Log($"[Sync] Complete at {LastSyncTime:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Sync] Failed", ex);
            WeakReferenceMessenger.Default.Send(new SyncFailedMessage(new SyncErrorEventArgs
            {
                ErrorMessage = ex.Message,
                Exception = ex
            }));
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();
        }
    }

    public async Task SyncLibrariesAsync()
    {
        // Coalesce concurrent callers: whoever arrives while a sync is running waits
        // for it and then returns — the data is fresh already, no second download.
        if (!await _librariesLock.WaitAsync(0))
        {
            _logger.LogDebug("[Sync] Library sync already running — joining instead of duplicating");
            await _librariesLock.WaitAsync();
            _librariesLock.Release();
            return;
        }

        try
        {
            var libraries = await _apiService.GetLibrariesAsync();

            if (libraries.Count == 0)
            {
                // An empty answer is indistinguishable from a failed request at this level
                // (401 / timeout / parse error all come back as an empty list), so never
                // treat it as authoritative: keep whatever is cached locally.
                _logger.LogWarning(
                    $"[Sync] Server returned no libraries (unauthorized={_apiService.LastRequestUnauthorized}) — keeping cached data");
                return;
            }

            await _database.SaveLibrariesAsync(libraries);

            // Pre-load all existing books to avoid N+1 queries
            var allExisting = await _database.GetAllAudioBooksAsync();
            var existingLookup = allExisting.ToDictionary(b => b.Id);

            foreach (var library in libraries)
            {
                var items = await _apiService.GetLibraryItemsAsync(library.Id);

                if (items.Count == 0)
                {
                    _logger.LogWarning(
                        $"[Sync] No items for library '{library.Name}' (unauthorized={_apiService.LastRequestUnauthorized}) — keeping cached books");
                    continue;
                }

                // Preserve local state (download info + progress) from existing DB records
                foreach (var item in items)
                {
                    item.LibraryId = library.Id;
                    existingLookup.TryGetValue(item.Id, out var existingItem);
                    if (existingItem != null)
                    {
                        item.IsDownloaded = existingItem.IsDownloaded;
                        item.LocalPath = existingItem.LocalPath;

                        // Preserve progress — server library endpoint doesn't include it
                        if (existingItem.Progress > 0 || existingItem.CurrentTime.TotalSeconds > 0)
                        {
                            item.CurrentTime = existingItem.CurrentTime;
                            item.Progress = existingItem.Progress;
                            item.IsFinished = existingItem.IsFinished;
                        }

                        // Library items endpoint doesn't return audioFiles — preserve from DB
                        if (item.AudioFiles.Count == 0 && existingItem.AudioFiles.Count > 0)
                        {
                            item.AudioFiles = existingItem.AudioFiles;
                            _logger.LogDebug($"[Sync] Preserved {existingItem.AudioFiles.Count} AudioFiles from DB for '{item.Title}' (server returned none)");
                        }
                        else if (item.AudioFiles.Count > 0)
                        {
                            foreach (var audioFile in item.AudioFiles)
                            {
                                var existingFile = FindMatchingAudioFile(audioFile, existingItem.AudioFiles);
                                if (existingFile != null && !string.IsNullOrEmpty(existingFile.LocalPath))
                                    audioFile.LocalPath = existingFile.LocalPath;
                            }

                            var preservedCount = item.AudioFiles.Count(f => !string.IsNullOrEmpty(f.LocalPath));
                            if (existingItem.IsDownloaded && preservedCount == 0)
                                _logger.LogWarning($"[Sync] All LocalPaths lost for '{item.Title}' (server: {item.AudioFiles.Count}, db: {existingItem.AudioFiles.Count})");
                        }
                    }

                    // If still not marked as downloaded, try to detect files on disk
                    // (self-healing after cache clear or DB corruption)
                    if (!item.IsDownloaded)
                    {
                        TryRecoverDownloadState(item);
                    }
                }

                await _database.SaveAudioBooksAsync(items);

                // Seed PlaybackProgress table from cached library data so Nine Lives works
                // Only seed if NO local progress exists yet — never overwrite existing entries
                int seeded = 0;
                foreach (var item in items)
                {
                    if (item.CurrentTime.TotalSeconds <= 0 && item.Progress <= 0) continue;
                    if (_activeItemId != null && item.Id == _activeItemId) continue;

                    // Don't overwrite existing progress entries — they have real timestamps
                    var existing = await _database.GetPlaybackProgressAsync(item.Id);
                    if (existing != null) continue;

                    // If CurrentTime is 0 but we have Progress, estimate position from duration
                    var position = item.CurrentTime;
                    if (position.TotalSeconds <= 0 && item.Progress > 0 && item.Duration.TotalSeconds > 0)
                    {
                        position = TimeSpan.FromSeconds(item.Progress * item.Duration.TotalSeconds);
                    }

                    await _database.SavePlaybackProgressAsync(
                        item.Id,
                        position,
                        item.IsFinished);
                    seeded++;
                }
                if (seeded > 0)
                    _logger.Log($"[Sync] Seeded {seeded} new PlaybackProgress entries from cached library data");
            }

            _logger.Log($"[Sync] Synced {libraries.Count} libraries");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Sync] Library sync error", ex);
            throw;
        }
        finally
        {
            _librariesLock.Release();
        }
    }

    public async Task SyncProgressAsync()
    {
        try
        {
            var serverProgress = await _apiService.GetAllUserProgressAsync();
            int synced = 0;

            foreach (var progress in serverProgress)
            {
                var audioBook = await _database.GetAudioBookAsync(progress.LibraryItemId);
                if (audioBook == null) continue;

                // Skip if this is the actively-playing item — session owns progress
                if (_activeItemId != null && progress.LibraryItemId == _activeItemId)
                {
                    _logger.LogDebug($"[Sync] Skipping active item {progress.LibraryItemId} — session owns progress");
                    continue;
                }

                // Always update display-only fields (progress %, isFinished)
                audioBook.Progress = progress.Progress;
                audioBook.IsFinished = progress.IsFinished;

                // Only update position if server is strictly newer than local — newest wins
                var localProgress = await _database.GetPlaybackProgressWithTimestampAsync(progress.LibraryItemId);
                var localTimestamp = localProgress?.updatedAt ?? DateTime.MinValue;

                if (progress.LastUpdate > localTimestamp)
                {
                    var oldPosition = audioBook.CurrentTime;
                    audioBook.CurrentTime = progress.CurrentTime;
                    _logger.Log($"[Sync] POSITION CHANGE '{audioBook.Title}': {oldPosition} → {progress.CurrentTime} (server is newer: {progress.LastUpdate:O} > {localTimestamp:O})");

                    await _database.UpdateAudioBookAsync(audioBook);

                    // Use estimated position from progress*duration if currentTime is 0
                    var positionToSave = progress.CurrentTime;
                    if (positionToSave.TotalSeconds <= 0 && progress.Progress > 0 && audioBook.Duration.TotalSeconds > 0)
                    {
                        positionToSave = TimeSpan.FromSeconds(progress.Progress * audioBook.Duration.TotalSeconds);
                    }

                    await _database.SavePlaybackProgressAsync(
                        progress.LibraryItemId,
                        positionToSave,
                        progress.IsFinished,
                        progress.LastUpdate); // Use server's timestamp for correct Nine Lives sorting
                }
                else
                {
                    // Position stays local — only persist display fields
                    await _database.UpdateAudioBookAsync(audioBook);
                    _logger.LogDebug($"[Sync] Kept local position for '{audioBook.Title}': local is newer or same ({localTimestamp:O} >= {progress.LastUpdate:O})");
                }

                synced++;
            }

            _logger.Log($"[Sync] Progress synced for {synced} items (from {serverProgress.Count} server entries)");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Sync] Progress sync error", ex);
            throw;
        }
    }

    /// <summary>
    /// Called by playback service to indicate which item is actively playing.
    /// While active, periodic sync skips this item (session owns progress).
    /// </summary>
    public void SetActivePlaybackItem(string? itemId)
    {
        _activeItemId = itemId;
        _lastSyncedTime = 0;
        _lastSyncTimestamp = DateTime.MinValue;

        if (itemId != null)
            _logger.LogDebug($"[Sync] Active playback item: {itemId}");
        else
            _logger.LogDebug("[Sync] Active playback item cleared");
    }

    /// <summary>
    /// Called by playback position timer. Throttles sync: only pushes if
    /// position advanced > 2s since last sync OR > 1% progress, AND at least
    /// 30s since last push.
    /// </summary>
    public async Task ReportPlaybackPositionAsync(string itemId, double currentTime, double duration, bool isFinished = false)
    {
        if (!_settingsService.Settings.AutoSyncProgress) return;
        if (!_apiService.IsAuthenticated) return;
        if (string.IsNullOrEmpty(itemId)) return;

        // Always save locally regardless of connectivity
        try
        {
            await _database.SavePlaybackProgressAsync(
                itemId,
                TimeSpan.FromSeconds(currentTime),
                isFinished);
        }
        catch { /* best effort local save */ }

        // Skip network call if offline — no 30s hang
        if (!_connectivity.IsServerReachable) return;

        var now = DateTime.UtcNow;
        var timeSinceLastSync = now - _lastSyncTimestamp;
        var positionDelta = Math.Abs(currentTime - _lastSyncedTime);
        var progressDelta = duration > 0 ? positionDelta / duration : 0;

        // Throttle: must meet both time AND delta thresholds (unless finished)
        if (!isFinished)
        {
            if (timeSinceLastSync < MinSyncInterval) return;
            if (positionDelta < MinPositionDelta && progressDelta < MinProgressDelta) return;
        }

        try
        {
            await _apiService.UpdateProgressAsync(itemId, currentTime, isFinished);

            _lastSyncedTime = currentTime;
            _lastSyncTimestamp = now;

            _logger.LogDebug($"[Sync] Progress pushed: {currentTime:F1}s (delta {positionDelta:F1}s)");
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Sync] Progress push failed: {ex.Message}");
            // Non-fatal — will retry on next tick
        }
    }

    /// <summary>
    /// Force-push final progress on stop/exit regardless of throttle.
    /// </summary>
    public async Task FlushPlaybackProgressAsync(string itemId, double currentTime, double duration, bool isFinished)
    {
        // Always save locally first (crash safety)
        try
        {
            await _database.SavePlaybackProgressAsync(
                itemId,
                TimeSpan.FromSeconds(currentTime),
                isFinished);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Sync] Local progress save failed: {ex.Message}");
        }

        if (!_apiService.IsAuthenticated || !_connectivity.IsServerReachable)
        {
            // Offline: enqueue for upload when connectivity returns
            if (_apiService.IsAuthenticated)
            {
                try { await _offlineQueue.EnqueueAsync(itemId, currentTime, isFinished); }
                catch { /* best effort */ }
                _logger.LogDebug($"[Sync] Offline — enqueued progress for {itemId} @ {currentTime:F1}s");
            }
            SetActivePlaybackItem(null);
            return;
        }

        try
        {
            await _apiService.UpdateProgressAsync(itemId, currentTime, isFinished);
            _logger.Log($"[Sync] Final progress flush: {itemId} @ {currentTime:F1}s (finished={isFinished})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Sync] Final progress flush failed: {ex.Message}");
            // Enqueue for retry when online
            try { await _offlineQueue.EnqueueAsync(itemId, currentTime, isFinished); }
            catch { /* best effort */ }
        }

        SetActivePlaybackItem(null);
    }

    /// <summary>
    /// Checks if downloaded audio files exist on disk for a book that isn't marked as downloaded.
    /// Recovers IsDownloaded, LocalPath, and AudioFile.LocalPath from the expected download directory.
    /// </summary>
    private void TryRecoverDownloadState(AudioBook item)
    {
        try
        {
            var actualPath = DownloadPathHelper.ResolveExistingPath(
                _settingsService.Settings.DownloadPath, item.Title, item.Author, item.Id);

            if (actualPath == null)
                return;

            if (item.AudioFiles != null && item.AudioFiles.Count > 0)
            {
                // Try to match each audio file to a file on disk
                int matched = 0;
                foreach (var af in item.AudioFiles)
                {
                    var rawName = af.Filename ?? $"part_{af.Index + 1}.m4b";
                    var fileName = Path.GetFileName(rawName);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    var filePath = Path.Combine(actualPath, fileName);
                    if (File.Exists(filePath))
                    {
                        af.LocalPath = filePath;
                        matched++;
                    }
                }

                if (matched > 0)
                {
                    item.IsDownloaded = true;
                    item.LocalPath = actualPath;
                    _logger.Log($"[Sync] Recovered download state for '{item.Title}': {matched}/{item.AudioFiles.Count} files on disk");
                }
            }
            else
            {
                // No AudioFiles metadata — scan directory for audio files
                var audioExtensions = new[] { ".m4b", ".m4a", ".mp3", ".ogg", ".opus", ".flac", ".wma", ".aac" };
                var files = Directory.GetFiles(actualPath)
                    .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count > 0)
                {
                    item.AudioFiles = files.Select((f, idx) => new AudioFile
                    {
                        Id = idx.ToString(),
                        Ino = string.Empty,
                        Index = idx,
                        Filename = Path.GetFileName(f),
                        LocalPath = f,
                        Duration = TimeSpan.Zero
                    }).ToList();

                    item.IsDownloaded = true;
                    item.LocalPath = actualPath;
                    _logger.Log($"[Sync] Recovered download state for '{item.Title}': {files.Count} audio file(s) found on disk (no prior AudioFiles metadata)");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Sync] Download recovery check failed for '{item.Title}': {ex.Message}");
        }
    }

    /// <summary>
    /// Finds the best matching existing AudioFile from the DB version for a server AudioFile.
    /// Priority: Ino match (if non-empty) > Filename match > Index match.
    /// </summary>
    private static AudioFile? FindMatchingAudioFile(AudioFile serverFile, List<AudioFile> existingFiles)
    {
        // Priority 1: Ino match (only if both are non-empty)
        if (!string.IsNullOrEmpty(serverFile.Ino))
        {
            var inoMatch = existingFiles.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.Ino) && f.Ino == serverFile.Ino);
            if (inoMatch != null) return inoMatch;
        }

        // Priority 2: Filename match (compare just the file name portion)
        if (!string.IsNullOrEmpty(serverFile.Filename))
        {
            var serverName = Path.GetFileName(serverFile.Filename);
            var filenameMatch = existingFiles.FirstOrDefault(f =>
                !string.IsNullOrEmpty(f.Filename) &&
                string.Equals(Path.GetFileName(f.Filename), serverName, StringComparison.OrdinalIgnoreCase));
            if (filenameMatch != null) return filenameMatch;
        }

        // Priority 3: Index match
        return existingFiles.FirstOrDefault(f => f.Index == serverFile.Index);
    }

    public void Dispose()
    {
        _syncTimer?.Dispose();
        _syncLock.Dispose();
        _librariesLock.Dispose();
    }
}
