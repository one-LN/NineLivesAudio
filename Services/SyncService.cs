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

    // The home page pulls on open while the timer may already be pulling: without this the
    // two runs would fetch the same payload twice and race on the database.
    private readonly SemaphoreSlim _progressLock = new(1, 1);

    // Guards the library pull so the startup sync and a page load cannot download
    // the same libraries twice at the same time.
    private readonly SemaphoreSlim _librariesLock = new(1, 1);

    // Progress coordinator state
    private string? _activeItemId;
    private string? _lastSyncedItemId;
    private double _lastSyncedTime;
    private DateTime _lastSyncTimestamp = DateTime.MinValue;
    private DateTime _lastProgressPullTimestamp = DateTime.MinValue;

    // Library metadata changes rarely. Downloading every library's item list on the same
    // one-minute cadence as progress pulled hundreds of KB per minute for nothing.
    private DateTime _lastLibrarySyncAt = DateTime.MinValue;

    /// <summary>Set while a full sync runs: it sends a single completion message itself.</summary>
    private bool _suppressProgressNotification;
    private static readonly TimeSpan LibrarySyncInterval = TimeSpan.FromMinutes(10);

    // When the session's first library pull had to step aside for playback.
    private DateTime _librarySyncDeferredAt = DateTime.MinValue;

    // When the last successful library pull finished (dedupe across callers).
    private DateTime _lastLibraryPullCompletedAt = DateTime.MinValue;

    /// <summary>
    /// True while audio is loading or playing. Resolved lazily: SyncService is constructed before
    /// the playback service, so it cannot ask for it in the constructor.
    /// </summary>
    private static bool IsPlaybackBusy()
    {
        try
        {
            if (App.Services?.GetService(typeof(IAudioPlaybackService)) is IAudioPlaybackService playback)
                return playback.State is PlaybackState.Playing or PlaybackState.Loading or PlaybackState.Buffering;
        }
        catch { /* container not built yet */ }

        return false;
    }
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

    // Guards the timer itself. Two callers landing together (start-up + the settings page
    // re-applying the schedule) used to create two timers and log "Started" twice.
    private readonly object _scheduleLock = new();

    public Task StartAsync()
    {
        lock (_scheduleLock)
        {
            if (_syncTimer != null) return Task.CompletedTask;
            StartCore();
        }

        return Task.CompletedTask;
    }

    private TimeSpan _currentInterval;

    private void StartCore()
    {

        var intervalMinutes = _settingsService.Settings.SyncIntervalMinutes;
        var interval = TimeSpan.FromMinutes(intervalMinutes > 0 ? intervalMinutes : 5);

        _syncTimer = new Timer(
            _ =>
            {
                if (!_settingsService.Settings.AutoSyncProgress) return;
                // Timer callback is async-void by design; SyncNowAsync already
                // guards re-entry via _syncLock and catches its own exceptions.
                _ = SyncNowAsync();
            },
            null,
            TimeSpan.FromSeconds(3), // Initial delay — fast first sync so Home page gets data quickly
            interval);

        _currentInterval = interval;
        _logger.Log($"[Sync] Started with {interval.TotalMinutes}min interval");
    }

    /// <summary>
    /// Restarts the periodic sync with the interval that is configured right now, or stops
    /// it when automatic syncing was switched off. Called from the settings page.
    /// </summary>
    public Task ApplyScheduleAsync()
    {
        lock (_scheduleLock)
        {
            var minutes = _settingsService.Settings.SyncIntervalMinutes;
            var desired = TimeSpan.FromMinutes(minutes > 0 ? minutes : 5);

            // The settings page re-applies the schedule every time it opens; recreating the timer
            // then logged "Started" twice in a row for no reason.
            if (_syncTimer != null && _currentInterval == desired)
            {
                _logger.LogDebug($"[Sync] Schedule unchanged ({desired.TotalMinutes}min)");
                return Task.CompletedTask;
            }

            _syncTimer?.Dispose();
            _syncTimer = null;

            if (!_settingsService.Settings.AutoSyncProgress)
            {
                _logger.Log("[Sync] Automatic sync disabled — schedule cleared");
                return Task.CompletedTask;
            }

            StartCore();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_scheduleLock)
        {
            _syncTimer?.Dispose();
            _syncTimer = null;
        }

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

            // Progress every tick; library metadata at most every 10 minutes. "Sync now" and
            // the first sync of a session still do both (see SyncLibrariesAsync callers).
            // While audio is loading or playing the pull is both rarer and, for the session's
            // first one, deferred: 309 items over four parallel requests racing the stream is
            // what turned a 1.4 s start into a 7 s one in the user's log.
            var playbackBusy = IsPlaybackBusy();

            var libraryInterval = playbackBusy
                ? TimeSpan.FromTicks(LibrarySyncInterval.Ticks * 3)
                : LibrarySyncInterval;

            var libraryDue = _lastLibrarySyncAt == DateTime.MinValue
                             || DateTime.UtcNow - _lastLibrarySyncAt >= libraryInterval;

            if (libraryDue && playbackBusy && _lastLibrarySyncAt == DateTime.MinValue)
            {
                if (_librarySyncDeferredAt == DateTime.MinValue)
                    _librarySyncDeferredAt = DateTime.UtcNow;

                if (DateTime.UtcNow - _librarySyncDeferredAt < TimeSpan.FromMinutes(10))
                {
                    _logger.LogDebug("[Sync] Playback active — first library pull deferred");
                    libraryDue = false;
                }
            }

            if (libraryDue)
            {
                _librarySyncDeferredAt = DateTime.MinValue;
                await SyncLibrariesAsync(force: true);
                _lastLibrarySyncAt = DateTime.UtcNow;
            }
            else if (_lastLibrarySyncAt != DateTime.MinValue)
            {
                _logger.LogDebug($"[Sync] Library metadata is {DateTime.UtcNow - _lastLibrarySyncAt} old — progress only");
            }

            _suppressProgressNotification = true;
            try { await SyncProgressAsync(); }
            finally { _suppressProgressNotification = false; }

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

    public void NotifyDataCleared()
    {
        WeakReferenceMessenger.Default.Send(new SyncCompletedMessage(new SyncEventArgs()));
    }

    public async Task SyncLibrariesAsync(bool force = false)
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

        // Three callers can ask for this within seconds — the home page, the library page and the
        // timer. Pulling 310 items three times is how one slow pull became several.
        if (!force &&
            _lastLibraryPullCompletedAt != DateTime.MinValue &&
            DateTime.UtcNow - _lastLibraryPullCompletedAt < TimeSpan.FromMinutes(2))
        {
            _logger.LogDebug($"[Sync] Library metadata pulled {DateTime.UtcNow - _lastLibraryPullCompletedAt:mm\\:ss} ago — skipping");
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
                var itemsWatch = System.Diagnostics.Stopwatch.StartNew();
                var items = await _apiService.GetLibraryItemsAsync(library.Id);
                _logger.Log($"[Sync] Library '{library.Name}': {items.Count} items in " +
                            $"{itemsWatch.ElapsedMilliseconds} ms ({_apiService.LastFetchDiagnostics})");

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

            _lastLibraryPullCompletedAt = DateTime.UtcNow;
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
        if (!await _progressLock.WaitAsync(0))
        {
            _logger.LogDebug("[Sync] Progress pull already running, skipping duplicate");
            return;
        }

        try
        {
            await SyncProgressCoreAsync();
        }
        finally
        {
            _progressLock.Release();
        }
    }

    private async Task SyncProgressCoreAsync()
    {
        try
        {
            var serverProgress = await _apiService.GetAllUserProgressAsync();
            int synced = 0;
            int unchanged = 0;
            int missing = 0;

            // The watermark is compared against server timestamps, so it has to come *from*
            // the server too. Storing DateTime.UtcNow here meant any client clock that ran
            // ahead (a few seconds is enough) made every later update look "not newer" and
            // progress silently stopped refreshing until the next restart.
            var watermark = _lastProgressPullTimestamp;

            // Positions that this device listened to while offline and has not uploaded yet.
            // Those must not be overwritten by an older server value.
            HashSet<string> pendingLocal;
            try
            {
                pendingLocal = (await _database.GetPendingProgressAsync())
                    .Select(p => p.ItemId)
                    .ToHashSet(StringComparer.Ordinal);
            }
            catch
            {
                pendingLocal = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var progress in serverProgress)
            {
                // Nothing the server knows has changed since the previous pull, so there
                // is no reason to hit the database for this item (big libraries meant
                // hundreds of needless queries on every timer tick).
                if (progress.LastUpdate <= _lastProgressPullTimestamp)
                {
                    unchanged++;
                    continue;
                }

                var audioBook = await _database.GetAudioBookAsync(progress.LibraryItemId);
                if (audioBook == null)
                {
                    // Library not imported yet — retry this entry on the next pull.
                    missing++;
                    continue;
                }

                // Skip if this is the actively-playing item — session owns progress
                if (_activeItemId != null && progress.LibraryItemId == _activeItemId)
                {
                    _logger.LogDebug($"[Sync] Skipping active item {progress.LibraryItemId} — session owns progress");
                    continue;
                }

                // Always update display-only fields (progress %, isFinished)
                audioBook.Progress = progress.Progress;
                audioBook.IsFinished = progress.IsFinished;

                // The server is the source of truth for positions. This used to compare the
                // server timestamp with a locally written DateTime.UtcNow, so a client clock
                // running a few seconds ahead permanently "won" and the position never
                // refreshed — which is exactly why the shelf looked stale until you pressed
                // play (playback reads the position straight from the server).
                // The only exception is a local position that is still waiting to be uploaded.
                if (pendingLocal.Contains(progress.LibraryItemId))
                {
                    await _database.UpdateAudioBookAsync(audioBook);
                    _logger.LogDebug($"[Sync] Keeping local position for '{audioBook.Title}' — an offline update is still queued");
                    synced++;
                    continue;
                }

                var localTimestamp = DateTime.MinValue;
                try
                {
                    var localProgress = await _database.GetPlaybackProgressWithTimestampAsync(progress.LibraryItemId);
                    localTimestamp = localProgress?.updatedAt ?? DateTime.MinValue;
                }
                catch { /* informational only */ }

                {
                    var oldPosition = audioBook.CurrentTime;
                    audioBook.CurrentTime = progress.CurrentTime;

                    // Only worth an INFO line when the position really moved; the startup pull
                    // logs every book and used to add a dozen "0.0000001s" lines per launch.
                    var delta = (oldPosition - progress.CurrentTime).Duration();
                    if (delta > TimeSpan.FromSeconds(1))
                        _logger.Log($"[Sync] POSITION CHANGE '{audioBook.Title}': {oldPosition} → {progress.CurrentTime} " +
                                    $"(server {progress.LastUpdate:O}, local {localTimestamp:O})");
                    else
                        _logger.LogDebug($"[Sync] Position refreshed for '{audioBook.Title}' (no audible change)");

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
                // LastUpdate is DateTime? on the API model; the comparison above already
                // guarantees a value here.
                if (progress.LastUpdate > watermark && progress.LastUpdate.HasValue)
                    watermark = progress.LastUpdate.Value;
                synced++;
            }

            // Only remember the watermark when every entry was applied: an entry that was
            // skipped because its book is not imported yet must be retried next time.
            if (missing == 0)
                _lastProgressPullTimestamp = watermark;

            _logger.Log($"[Sync] Progress synced for {synced} of {serverProgress.Count} server entries " +
                        $"({unchanged} unchanged, {missing} awaiting library import), " +
                        $"watermark {watermark:yyyy-MM-dd HH:mm:ss}Z");

            // Anyone who asked for this pull (e.g. the home page on open) gets the fresh
            // numbers without waiting for the next timer tick. A full SyncNowAsync sends its
            // own message afterwards, so it suppresses this one.
            if (synced > 0 && !_suppressProgressNotification)
                WeakReferenceMessenger.Default.Send(new SyncCompletedMessage(new SyncEventArgs { ItemsSynced = synced }));
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

        // Throttling is per book: switching books must not inherit the previous
        // book's timestamp (that delayed the first push of a new book by up to 30s).
        if (!string.Equals(_lastSyncedItemId, itemId, StringComparison.Ordinal))
        {
            _lastSyncedItemId = itemId;
            _lastSyncedTime = 0;
            _lastSyncTimestamp = DateTime.MinValue;
        }

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
            await _apiService.UpdateProgressAsync(itemId, currentTime, isFinished, duration);

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
            await _apiService.UpdateProgressAsync(itemId, currentTime, isFinished, duration);
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
