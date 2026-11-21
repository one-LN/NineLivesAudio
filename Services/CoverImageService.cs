using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using NineLivesAudio.Helpers;

namespace NineLivesAudio.Services;

/// <summary>
/// Centralized cover image loading with an in-memory cache and decode-size
/// limits so list scrolling does not repeatedly decode full-resolution art.
/// </summary>
public static class CoverImageService
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();
    private const int MaxCached = 300;

    // Cover art used to be requested from the server every single time a card was realised
    // (the token in the URL even changed on relogin, defeating WinUI's own HTTP cache). Now the
    // first sighting downloads it into <data>\covers and every later load comes from disk,
    // so covers survive going offline and the folder is part of a portable install.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly ConcurrentDictionary<string, byte> Downloads = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim DownloadSlots = new(4, 4);
    private static int _storedCount;

    /// <summary>
    /// Loads a BitmapImage from a cover path URI, returning null on failure.
    /// Results are cached; decode size is capped to the card size.
    /// </summary>
    public static BitmapImage? LoadCover(string? coverPath, int decodeWidth = 400, int decodeHeight = 400)
    {
        if (string.IsNullOrEmpty(coverPath))
            return null;

        // Keyed by the cover's identity (item id), not the raw URL: the URL carries the auth
        // token, so every relogin used to throw away the decoded bitmaps.
        var cacheKey = $"{KeyFor(coverPath)}|{decodeWidth}x{decodeHeight}";
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var source = ResolveSource(coverPath);
            var image = new BitmapImage(new Uri(source))
            {
                DecodePixelWidth = decodeWidth,
                DecodePixelHeight = decodeHeight,
            };
            if (Cache.Count < MaxCached)
                Cache[cacheKey] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Local file when it is already cached, otherwise the remote URL — and the download is
    /// scheduled in the background so the next sighting is local.
    /// </summary>
    private static string ResolveSource(string coverPath)
    {
        if (!coverPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return coverPath; // already a local file (downloaded book)

        var file = LocalFileFor(coverPath);
        if (file != null && File.Exists(file))
            return file;

        if (file != null)
            QueueDownload(coverPath, file);

        return coverPath;
    }

    private static string? LocalFileFor(string url)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.CoversDirectory);
            return Path.Combine(AppPaths.CoversDirectory, KeyFor(url) + ".img");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Item id when the URL is a cover endpoint, otherwise a hash of the URL.</summary>
    private static string KeyFor(string url)
    {
        var marker = "/api/items/";
        var at = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            var rest = url[(at + marker.Length)..];
            var slash = rest.IndexOf('/');
            if (slash > 0)
            {
                var id = rest[..slash];
                if (id.Length is > 0 and < 64 && id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
                    return "item_" + id;
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return "url_" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static void QueueDownload(string url, string targetPath)
    {
        if (!Downloads.TryAdd(targetPath, 0)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                // Respect the "auto-download cover art" setting (default on).
                ISettingsService? settings = null;
                try { settings = App.Services?.GetService(typeof(ISettingsService)) as ISettingsService; }
                catch { /* services not ready yet — treat as enabled */ }

                if (settings != null && !settings.Settings.AutoDownloadCovers) return;

                // Covers are the least urgent download in the app. While audio is playing, give
                // the link to the player first — 300 covers at once is otherwise a bandwidth
                // fight the stream loses.
                if (IsPlaybackActive())
                    await Task.Delay(1500).ConfigureAwait(false);

                await DownloadSlots.WaitAsync().ConfigureAwait(false);
                try
                {
                    var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                    if (bytes.Length == 0) return;

                    var temp = targetPath + ".part";
                    await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
                    File.Move(temp, targetPath, overwrite: true);

                    // Every 25th cover prints a line: the folder should be visible in <data>.
                    var stored = Interlocked.Increment(ref _storedCount);
                    if (stored % 25 == 0)
                    {
                        try
                        {
                            var logger = App.Services?.GetService(typeof(ILoggingService)) as ILoggingService;
                            logger?.Log($"[Covers] {stored} cover(s) stored in {AppPaths.CoversDirectory}");
                        }
                        catch { /* logging is optional */ }
                    }
                }
                finally
                {
                    DownloadSlots.Release();
                }
            }
            catch { /* a cover that will not download is not an error worth surfacing */ }
            finally
            {
                Downloads.TryRemove(targetPath, out _);
            }
        });
    }

    /// <summary>
    /// Loads a thumbnail-sized BitmapImage by appending width/height query parameters.
    /// Used for mini player and other compact views.
    /// </summary>
    public static BitmapImage? LoadThumbnail(string? coverPath, int width = 100, int height = 100)
    {
        if (string.IsNullOrEmpty(coverPath))
            return null;

        var cacheKey = $"{KeyFor(coverPath)}|thumb{width}x{height}";
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var thumbnailUri = coverPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? (coverPath.Contains('?')
                    ? $"{coverPath}&width={width}&height={height}"
                    : $"{coverPath}?width={width}&height={height}")
                : coverPath;
            var image = new BitmapImage(new Uri(ResolveSource(thumbnailUri)))
            {
                DecodePixelWidth = width,
                DecodePixelHeight = height,
            };
            if (Cache.Count < MaxCached)
                Cache[cacheKey] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Drops the in-memory images; the files on disk stay (that is the point).</summary>
    /// <summary>Clears the cover cache (e.g. on logout / cache clear).</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// Deletes the downloaded covers from disk. They are permanent by design (they survive
    /// restarts and updates), so this only runs when the user asks for it.
    /// </summary>
    public static void ClearDiskCache()
    {
        ClearCache();

        try
        {
            if (!Directory.Exists(AppPaths.CoversDirectory)) return;

            var deleted = 0;
            foreach (var file in Directory.GetFiles(AppPaths.CoversDirectory, "*.img"))
            {
                try { File.Delete(file); deleted++; }
                catch { /* in use */ }
            }

            _storedCount = 0;
            Log("[Covers] cleared {0} cover file(s) from {1}", deleted, AppPaths.CoversDirectory);
        }
        catch { /* nothing to clear */ }
    }

    /// <summary>One line at start-up so the folder's size can be checked from the log.</summary>
    public static void LogCacheSummary()
    {
        try
        {
            if (!Directory.Exists(AppPaths.CoversDirectory))
            {
                Log("[Covers] cache is empty ({0})", AppPaths.CoversDirectory);
                return;
            }

            var files = Directory.GetFiles(AppPaths.CoversDirectory, "*.img");
            long bytes = 0;
            foreach (var file in files)
            {
                try { bytes += new FileInfo(file).Length; } catch { /* vanished */ }
            }

            Log("[Covers] {0} cover(s), {1:0.0} MB on disk in {2}",
                files.Length, bytes / 1024.0 / 1024.0, AppPaths.CoversDirectory);
        }
        catch { /* logging is optional */ }
    }

    private static bool IsPlaybackActive()
    {
        try
        {
            if (App.Services?.GetService(typeof(IAudioPlaybackService)) is IAudioPlaybackService playback)
                return playback.State is PlaybackState.Playing or PlaybackState.Loading or PlaybackState.Buffering;
        }
        catch { /* services not ready */ }

        return false;
    }

    private static void Log(string template, params object[] args)
    {
        try
        {
            var logger = App.Services?.GetService(typeof(ILoggingService)) as ILoggingService;
            logger?.Log(string.Format(template, args));
        }
        catch { /* logging is optional */ }
    }
}
