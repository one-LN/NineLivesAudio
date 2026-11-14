using NineLivesAudio.Helpers;
using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface IPlaybackSourceResolver
{
    /// <summary>
    /// Determines the best playback source for an audiobook.
    /// Priority: Local downloaded file > Stream from server > Error
    /// </summary>
    PlaybackSource ResolveSource(AudioBook audioBook);
}

public enum PlaybackSourceType
{
    LocalFile,
    Stream,
    Unavailable
}

public class PlaybackSource
{
    public PlaybackSourceType Type { get; set; }
    public string? LocalFilePath { get; set; }
    public string? StreamItemId { get; set; }
    public string? ErrorMessage { get; set; }
    public bool WasRecoveredFromDisk { get; set; }
    public bool IsDownloaded => Type == PlaybackSourceType.LocalFile;

    public static PlaybackSource FromLocal(string filePath) => new()
    {
        Type = PlaybackSourceType.LocalFile,
        LocalFilePath = filePath
    };

    public static PlaybackSource FromStream(string itemId) => new()
    {
        Type = PlaybackSourceType.Stream,
        StreamItemId = itemId
    };

    public static PlaybackSource Unavailable(string error) => new()
    {
        Type = PlaybackSourceType.Unavailable,
        ErrorMessage = error
    };
}

public class PlaybackSourceResolver : IPlaybackSourceResolver
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;

    public PlaybackSourceResolver(
        IAudioBookshelfApiService apiService,
        ISettingsService settingsService,
        ILoggingService loggingService,
        ILocalizationService localization)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _logger = loggingService;
        _localization = localization;
    }

    public PlaybackSource ResolveSource(AudioBook audioBook)
    {
        if (audioBook == null)
        {
            _logger.LogWarning("[PlaybackSourceResolver] AudioBook is null");
            return PlaybackSource.Unavailable(_localization.GetString("NoAudiobookProvided"));
        }

        // Check for local downloaded file first
        var localSource = TryResolveLocalSource(audioBook);
        if (localSource != null)
        {
            _logger.Log($"[PlaybackSourceResolver] Using LOCAL file for '{audioBook.Title}'");
            return localSource;
        }

        // Fall back to streaming
        if (_apiService.IsAuthenticated)
        {
            _logger.Log($"[PlaybackSourceResolver] Using STREAM for '{audioBook.Title}'");
            return PlaybackSource.FromStream(audioBook.Id);
        }

        _logger.LogWarning($"[PlaybackSourceResolver] UNAVAILABLE - not downloaded and not authenticated for '{audioBook.Title}'");
        return PlaybackSource.Unavailable(_localization.GetString("BookNotDownloaded"));
    }

    private PlaybackSource? TryResolveLocalSource(AudioBook audioBook)
    {
        // If marked as downloaded, check the stored local paths
        if (audioBook.IsDownloaded && audioBook.AudioFiles?.Count > 0)
        {
            foreach (var audioFile in audioBook.AudioFiles.OrderBy(f => f.Index))
            {
                if (!string.IsNullOrEmpty(audioFile.LocalPath) && File.Exists(audioFile.LocalPath))
                {
                    _logger.Log($"[PlaybackSourceResolver] Found valid local file: {audioFile.LocalPath}");
                    return PlaybackSource.FromLocal(audioFile.LocalPath);
                }
            }

            var withPath = audioBook.AudioFiles.Count(f => !string.IsNullOrEmpty(f.LocalPath));
            _logger.LogWarning($"[PlaybackSourceResolver] IsDownloaded=true but no valid local files for '{audioBook.Title}'. " +
                $"AudioFiles: {audioBook.AudioFiles.Count}, WithLocalPath: {withPath}");
        }

        // Even if not marked downloaded, scan disk — DB may be stale (after cache clear, before sync)
        return TryScanDiskForLocalFiles(audioBook);
    }

    /// <summary>
    /// Scans the expected download directory on disk for audio files.
    /// Handles the case where DB lost download state but files still exist.
    /// </summary>
    private PlaybackSource? TryScanDiskForLocalFiles(AudioBook audioBook)
    {
        try
        {
            var actualPath = DownloadPathHelper.ResolveExistingPath(
                _settingsService.Settings.DownloadPath, audioBook.Title, audioBook.Author, audioBook.Id);

            if (actualPath == null) return null;

            // If we have AudioFiles metadata, match by filename
            if (audioBook.AudioFiles?.Count > 0)
            {
                foreach (var af in audioBook.AudioFiles.OrderBy(f => f.Index))
                {
                    var rawName = af.Filename ?? $"part_{af.Index + 1}.m4b";
                    var fileName = Path.GetFileName(rawName);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    var filePath = Path.Combine(actualPath, fileName);
                    if (File.Exists(filePath))
                    {
                        // Recover state on the audioBook object so BuildLocalTrackList works
                        af.LocalPath = filePath;
                        audioBook.IsDownloaded = true;
                        audioBook.LocalPath = actualPath;

                        // Also recover remaining files
                        foreach (var otherAf in audioBook.AudioFiles.Where(f => f != af))
                        {
                            var otherName = Path.GetFileName(otherAf.Filename ?? $"part_{otherAf.Index + 1}.m4b");
                            if (string.IsNullOrEmpty(otherName)) continue;
                            var otherPath = Path.Combine(actualPath, otherName);
                            if (File.Exists(otherPath))
                                otherAf.LocalPath = otherPath;
                        }

                        _logger.Log($"[PlaybackSourceResolver] Disk scan found local file: {filePath}");
                        var result = PlaybackSource.FromLocal(filePath);
                        result.WasRecoveredFromDisk = true;
                        return result;
                    }
                }
            }
            else
            {
                // No AudioFiles metadata at all (library endpoint doesn't return them).
                // Scan the download directory for any playable audio files.
                var audioExtensions = new[] { ".m4b", ".m4a", ".mp3", ".ogg", ".opus", ".flac", ".wma", ".aac" };
                var files = Directory.GetFiles(actualPath)
                    .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count > 0)
                {
                    _logger.Log($"[PlaybackSourceResolver] Disk scan (no AudioFiles metadata) found {files.Count} audio file(s) in {actualPath}");

                    // Reconstruct AudioFiles from disk
                    audioBook.AudioFiles = files.Select((f, idx) => new Models.AudioFile
                    {
                        Id = idx.ToString(),
                        Ino = string.Empty,
                        Index = idx,
                        Filename = Path.GetFileName(f),
                        LocalPath = f,
                        Duration = TimeSpan.Zero // will be determined during playback
                    }).ToList();

                    audioBook.IsDownloaded = true;
                    audioBook.LocalPath = actualPath;

                    var result = PlaybackSource.FromLocal(files[0]);
                    result.WasRecoveredFromDisk = true;
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[PlaybackSourceResolver] Disk scan failed: {ex.Message}");
        }

        return null;
    }

}
