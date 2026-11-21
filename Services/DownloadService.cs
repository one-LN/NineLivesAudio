using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Data;
using NineLivesAudio.Helpers;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using System.Collections.Concurrent;

namespace NineLivesAudio.Services;

public class DownloadService : IDownloadService, IDisposable
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalDatabase _database;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;
    private readonly ConcurrentDictionary<string, DownloadItem> _activeDownloads = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _downloadCts = new();
    private readonly SemaphoreSlim _downloadSemaphore = new(2);

    public DownloadService(
        IAudioBookshelfApiService apiService,
        ISettingsService settingsService,
        ILocalDatabase database,
        ILoggingService loggingService,
        ILocalizationService localization)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _database = database;
        _logger = loggingService;
        _localization = localization;

        _ = Task.Run(CleanupOrphanedPartsAsync);
    }

    public async Task<DownloadItem> QueueDownloadAsync(AudioBook audioBook)
    {
        _logger.Log($"[Download] Queuing: {audioBook.Title} ({audioBook.AudioFiles.Count} files)");

        var downloadItem = new DownloadItem
        {
            Id = Guid.NewGuid().ToString(),
            AudioBookId = audioBook.Id,
            Title = audioBook.Title,
            Status = DownloadStatus.Queued,
            StartedAt = DateTime.Now,
            FilesToDownload = audioBook.AudioFiles.Select(f => f.Ino).ToList()
        };

        _activeDownloads[downloadItem.Id] = downloadItem;
        await _database.SaveDownloadItemAsync(downloadItem);

        var cts = new CancellationTokenSource();
        _downloadCts[downloadItem.Id] = cts;

        _ = Task.Run(() => ProcessDownloadAsync(downloadItem, audioBook, cts.Token));
        return downloadItem;
    }

    public Task PauseDownloadAsync(string downloadId)
    {
        if (_activeDownloads.TryGetValue(downloadId, out var download))
        {
            download.Status = DownloadStatus.Paused;
            if (_downloadCts.TryGetValue(downloadId, out var cts))
                cts.Cancel();
            _logger.Log($"[Download] Paused: {download.Title}");
        }
        return Task.CompletedTask;
    }

    public async Task ResumeDownloadAsync(string downloadId)
    {
        if (_activeDownloads.TryGetValue(downloadId, out var download))
        {
            download.Status = DownloadStatus.Queued;

            if (_downloadCts.TryRemove(downloadId, out var previousCts))
            {
                previousCts.Cancel();
                previousCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            _downloadCts[downloadId] = cts;

            var audioBook = await _database.GetAudioBookAsync(download.AudioBookId);
            if (audioBook != null)
                _ = Task.Run(() => ProcessDownloadAsync(download, audioBook, cts.Token));

            _logger.Log($"[Download] Resuming: {download.Title}");
        }
    }

    public Task CancelDownloadAsync(string downloadId)
    {
        if (_activeDownloads.TryRemove(downloadId, out var download))
        {
            download.Status = DownloadStatus.Cancelled;
            if (_downloadCts.TryRemove(downloadId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            _logger.Log($"[Download] Cancelled: {download.Title}");
        }
        return Task.CompletedTask;
    }

    public Task<List<DownloadItem>> GetActiveDownloadsAsync()
        => Task.FromResult(_activeDownloads.Values.ToList());

    public async Task<bool> IsBookDownloadedAsync(string audioBookId)
    {
        var audioBook = await _database.GetAudioBookAsync(audioBookId);
        return audioBook?.IsDownloaded ?? false;
    }

    public async Task DeleteDownloadAsync(string audioBookId)
    {
        _logger.Log($"[Download] Deleting: {audioBookId}");
        var audioBook = await _database.GetAudioBookAsync(audioBookId);
        if (audioBook == null) return;

        // Try new logical path first (Author - Title), then legacy path (audioBookId)
        var logicalPath = GetDownloadPath(audioBook);
        var legacyPath = GetLegacyDownloadPath(audioBookId);

        if (Directory.Exists(logicalPath))
        {
            _logger.Log($"[Download] Removing folder: {logicalPath}");
            Directory.Delete(logicalPath, true);
        }
        if (Directory.Exists(legacyPath))
        {
            _logger.Log($"[Download] Removing legacy folder: {legacyPath}");
            Directory.Delete(legacyPath, true);
        }

        audioBook.IsDownloaded = false;
        audioBook.LocalPath = null;
        foreach (var file in audioBook.AudioFiles) file.LocalPath = null;
        await _database.UpdateAudioBookAsync(audioBook);
    }

    private async Task ProcessDownloadAsync(DownloadItem downloadItem, AudioBook audioBook, CancellationToken ct)
    {
        try
        {
            await _downloadSemaphore.WaitAsync(ct);
            try
            {
                downloadItem.Status = DownloadStatus.Downloading;
                _logger.Log($"[Download] Starting: {audioBook.Title}");

            var downloadPath = GetDownloadPath(audioBook);
            Directory.CreateDirectory(downloadPath);
            long downloadedBytes = 0;

            if (_settingsService.Settings.AutoDownloadCovers)
                await DownloadCoverAsync(audioBook.Id, downloadPath);

            // Library endpoint doesn't return audioFiles — fetch full details if needed
            if (audioBook.AudioFiles.Count == 0)
            {
                _logger.Log($"[Download] No AudioFiles cached, fetching full details for: {audioBook.Title}");
                var fullBook = await _apiService.GetAudioBookAsync(audioBook.Id);
                if (fullBook == null || fullBook.AudioFiles.Count == 0)
                {
                    _logger.LogWarning($"[Download] API returned no audio files for: {audioBook.Title}");
                    downloadItem.Status = DownloadStatus.Failed;
                    downloadItem.ErrorMessage = _localization.GetString("NoAudioFiles");
                    await _database.UpdateDownloadItemAsync(downloadItem);
                    WeakReferenceMessenger.Default.Send(new DownloadFailedMessage(downloadItem));
                    return;
                }
                audioBook.AudioFiles = fullBook.AudioFiles;
                _logger.Log($"[Download] Fetched {audioBook.AudioFiles.Count} audio files from API");
            }

            // Pre-compute TotalBytes before download loop for accurate progress
            downloadItem.TotalBytes = audioBook.AudioFiles.Sum(f => f.Size);
            if (downloadItem.TotalBytes == 0)
                downloadItem.TotalBytes = (long)(audioBook.AudioFiles.Sum(f => f.Duration.TotalSeconds) * 16000); // ~128kbps fallback
            _logger.Log($"[Download] Estimated total: {downloadItem.TotalBytes / 1024}KB for {audioBook.AudioFiles.Count} files");
            await _database.UpdateDownloadItemAsync(downloadItem);

            for (int i = 0; i < audioBook.AudioFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var audioFile = audioBook.AudioFiles[i];
                // Sanitize filename: API may return paths like "Disc 1/Track 01.mp3"
                var rawName = audioFile.Filename ?? $"part_{i + 1}.m4b";
                var fileName = Path.GetFileName(rawName);
                if (string.IsNullOrEmpty(fileName))
                    fileName = $"part_{i + 1}.m4b";
                var finalPath = Path.Combine(downloadPath, fileName);
                var partPath = finalPath + ".part";

                if (string.IsNullOrEmpty(audioFile.Ino))
                {
                    _logger.LogWarning($"[Download] Empty Ino for file '{fileName}' (index {i}), skipping");
                    continue;
                }

                _logger.Log($"[Download] File {i + 1}/{audioBook.AudioFiles.Count}: {fileName} (ino={audioFile.Ino})");
                _logger.Log($"[Download] Target path: {finalPath}");

                try
                {
                    using var stream = await _apiService.GetAudioFileStreamAsync(audioBook.Id, audioFile.Ino);
                    using var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

                    var buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                        downloadedBytes += bytesRead;
                        downloadItem.DownloadedBytes = downloadedBytes;

                        if (downloadedBytes % (512 * 1024) < 81920)
                        {
                            WeakReferenceMessenger.Default.Send(new DownloadProgressChangedMessage(new DownloadProgressEventArgs
                            {
                                DownloadId = downloadItem.Id,
                                Progress = downloadItem.Progress,
                                DownloadedBytes = downloadedBytes,
                                TotalBytes = downloadItem.TotalBytes
                            }));
                        }
                    }

                    // Atomic rename: .part -> final
                    fileStream.Close();
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    File.Move(partPath, finalPath);

                    audioFile.LocalPath = finalPath;
                    var fileSize = new FileInfo(finalPath).Length;
                    _logger.Log($"[Download] File done: {fileName} ({fileSize / 1024}KB)");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[Download] File failed: {fileName}", ex);
                    try { if (File.Exists(partPath)) File.Delete(partPath); } catch { /* best-effort: ignore */ }

                    downloadItem.RetryCount++;
                    if (downloadItem.RetryCount < downloadItem.MaxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, downloadItem.RetryCount) * 5);
                        _logger.Log($"[Download] Retry {downloadItem.RetryCount}/{downloadItem.MaxRetries} in {delay.TotalSeconds}s");
                        await Task.Delay(delay, ct);
                        i--; // Retry the same file
                        continue;
                    }

                    downloadItem.Status = DownloadStatus.Failed;
                    downloadItem.ErrorMessage = $"{fileName}: {ex.Message}";
                    await _database.UpdateDownloadItemAsync(downloadItem);
                    WeakReferenceMessenger.Default.Send(new DownloadFailedMessage(downloadItem));
                    return;
                }
            }

            // Verify at least one audio file was actually written to disk
            var downloadedCount = audioBook.AudioFiles.Count(f => !string.IsNullOrEmpty(f.LocalPath) && File.Exists(f.LocalPath));
            if (downloadedCount == 0)
            {
                _logger.LogWarning($"[Download] No files on disk for: {audioBook.Title}");
                downloadItem.Status = DownloadStatus.Failed;
                downloadItem.ErrorMessage = _localization.GetString("NoFilesWritten");
                await _database.UpdateDownloadItemAsync(downloadItem);
                WeakReferenceMessenger.Default.Send(new DownloadFailedMessage(downloadItem));
                return;
            }
            _logger.Log($"[Download] Verified {downloadedCount}/{audioBook.AudioFiles.Count} files on disk");

            if (downloadItem.Status == DownloadStatus.Downloading)
            {
                downloadItem.Status = DownloadStatus.Completed;
                downloadItem.CompletedAt = DateTime.Now;
                audioBook.IsDownloaded = true;
                audioBook.LocalPath = downloadPath;

                await _database.UpdateAudioBookAsync(audioBook);
                await _database.UpdateDownloadItemAsync(downloadItem);

                _logger.Log($"[Download] Complete: {audioBook.Title}");
                WeakReferenceMessenger.Default.Send(new DownloadCompletedMessage(downloadItem));
                _activeDownloads.TryRemove(downloadItem.Id, out _);
                if (_downloadCts.TryRemove(downloadItem.Id, out var completedCts))
                {
                    completedCts.Dispose();
                }
            }
            }
            catch (OperationCanceledException)
            {
                if (downloadItem.Status != DownloadStatus.Cancelled)
                    downloadItem.Status = DownloadStatus.Paused;
                await _database.UpdateDownloadItemAsync(downloadItem);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Download] Process failed: {audioBook.Title}", ex);
                downloadItem.Status = DownloadStatus.Failed;
                downloadItem.ErrorMessage = ex.Message;
                await _database.UpdateDownloadItemAsync(downloadItem);
                WeakReferenceMessenger.Default.Send(new DownloadFailedMessage(downloadItem));
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled before semaphore acquired
            if (downloadItem.Status != DownloadStatus.Cancelled)
                downloadItem.Status = DownloadStatus.Paused;
            await _database.UpdateDownloadItemAsync(downloadItem);
        }
    }

    private async Task DownloadCoverAsync(string audioBookId, string downloadPath)
    {
        try
        {
            // Request reasonable size cover (400x400) instead of full resolution
            var coverData = await _apiService.GetCoverImageAsync(audioBookId, width: 400, height: 400);
            if (coverData != null)
                await File.WriteAllBytesAsync(Path.Combine(downloadPath, "cover.jpg"), coverData);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Download] Cover failed: {ex.Message}");
        }
    }

    private async Task CleanupOrphanedPartsAsync()
    {
        try
        {
            var basePath = _settingsService.Settings.DownloadPath;
            if (string.IsNullOrEmpty(basePath))
                basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "AudioBookshelf");

            if (!Directory.Exists(basePath)) return;

            var partFiles = Directory.GetFiles(basePath, "*.part", SearchOption.AllDirectories);
            foreach (var pf in partFiles)
            {
                _logger.LogWarning($"[Download] Orphaned .part removed: {pf}");
                try { File.Delete(pf); } catch { /* best-effort: ignore */ }
            }
        }
        catch { /* best-effort: ignore */ }
    }

    private string GetDownloadPath(AudioBook audioBook)
        => DownloadPathHelper.GetDownloadPath(_settingsService.Settings.DownloadPath, audioBook.Title, audioBook.Author, audioBook.Id);

    private string GetLegacyDownloadPath(string audioBookId)
        => DownloadPathHelper.GetLegacyDownloadPath(_settingsService.Settings.DownloadPath, audioBookId);

    public void Dispose()
    {
        foreach (var cts in _downloadCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _downloadCts.Clear();
        _downloadSemaphore.Dispose();
    }
}
