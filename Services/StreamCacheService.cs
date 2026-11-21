using System.Collections.Concurrent;
using System.Net.Http;
using NineLivesAudio.Helpers;

namespace NineLivesAudio.Services;

/// <summary>
/// Session-scoped audio cache for streamed playback.
/// <para>
/// Streaming pulls each track (one chapter per track) from the server. Keeping the track that
/// is playing — and the one that comes next — on disk means a rewind, a replay or the natural
/// track change does not go back to the network, while nothing accumulates across sessions:
/// the folder is emptied on start and on exit.
/// </para>
/// <para>
/// This is deliberately *not* a download: at most <see cref="FilesPerBook"/> tracks per book
/// (roughly two chapters), other books are dropped as soon as playback moves on, and the cache
/// never affects the stored position — progress is "track offset + position inside the track",
/// which is independent of where the bytes came from.
/// </para>
/// </summary>
public interface IStreamCacheService
{
    /// <summary>
    /// Returns the local path when this track is already cached (play it from disk). When it is
    /// not cached the track is queued for download in the background and null is returned, so
    /// the caller keeps streaming this time and plays from disk next time.
    /// </summary>
    Task<string?> PrepareTrackAsync(string itemId, int trackIndex, string url);

    /// <summary>Pre-fetches upcoming tracks (used right before/after a track change).</summary>
    void Prefetch(string itemId, IEnumerable<(int Index, string Url)> tracks);

    /// <summary>Drops every other book's cache when playback moves to a different book.</summary>
    void OnBookChanged(string itemId);

    /// <summary>Deletes the whole cache. Called on app start and on exit.</summary>
    Task ClearAsync();

    /// <summary>Bytes currently on disk (for the settings/diagnostics line).</summary>
    long CurrentSizeBytes();
}

public sealed class StreamCacheService : IStreamCacheService, IDisposable
{
    /// <summary>Two tracks: the one playing and the next one. Matches "one book, two chapters".</summary>
    private const int FilesPerBook = 2;

    private readonly ILoggingService _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _downloadSlots = new(2, 2);
    private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();

    public StreamCacheService(ILoggingService logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    private static string CacheRoot => AppPaths.StreamCacheDirectory;

    private static string FolderFor(string itemId)
    {
        // Item ids come from the server; keep them to a safe file-name alphabet anyway.
        var safe = new string(itemId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (safe.Length == 0) safe = "book";
        return Path.Combine(CacheRoot, safe);
    }

    private static string FileFor(string itemId, int trackIndex) =>
        Path.Combine(FolderFor(itemId), $"{trackIndex:D5}.audio");

    public Task<string?> PrepareTrackAsync(string itemId, int trackIndex, string url)
    {
        if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(url))
            return Task.FromResult<string?>(null);

        var path = FileFor(itemId, trackIndex);
        if (Exists(path))
            return Task.FromResult<string?>(path);

        // Do NOT queue the download here: the caller is about to stream this very track, and
        // pulling it twice at once halves the bandwidth available for starting playback.
        // The playback service prefetches current+next once the stream has opened.
        return Task.FromResult<string?>(null);
    }

    public void Prefetch(string itemId, IEnumerable<(int Index, string Url)> tracks)
    {
        if (string.IsNullOrEmpty(itemId)) return;

        foreach (var (index, url) in tracks)
        {
            if (string.IsNullOrEmpty(url)) continue;
            if (Exists(FileFor(itemId, index))) continue;
            QueueDownload(itemId, index, url);
        }
    }

    public void OnBookChanged(string itemId)
    {
        try
        {
            if (!Directory.Exists(CacheRoot)) return;

            var keep = Path.GetFileName(FolderFor(itemId));
            foreach (var dir in Directory.GetDirectories(CacheRoot))
            {
                if (string.Equals(Path.GetFileName(dir), keep, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { Directory.Delete(dir, recursive: true); }
                catch { /* a file may still be open by the player */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[StreamCache] Book cleanup failed: {ex.Message}");
        }
    }

    public Task ClearAsync()
    {
        try
        {
            // Never cancel _shutdown here: it is the token every download waits on, so
            // cancelling it on startup made the whole cache a no-op for the rest of the run
            // (that is why no track was ever reported as cached in the logs).
            if (Directory.Exists(CacheRoot))
                Directory.Delete(CacheRoot, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[StreamCache] Clear failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public long CurrentSizeBytes()
    {
        try
        {
            if (!Directory.Exists(CacheRoot)) return 0;
            return Directory.EnumerateFiles(CacheRoot, "*", SearchOption.AllDirectories)
                            .Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    private static bool Exists(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch { return false; }
    }

    private void QueueDownload(string itemId, int trackIndex, string url)
    {
        var key = $"{itemId}|{trackIndex}";
        if (_inFlight.ContainsKey(key)) return;

        var task = Task.Run(async () =>
        {
            try
            {
                await _downloadSlots.WaitAsync(_shutdown.Token).ConfigureAwait(false);
                try
                {
                    await DownloadAsync(itemId, trackIndex, url).ConfigureAwait(false);
                }
                finally
                {
                    _downloadSlots.Release();
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                _logger.LogDebug($"[StreamCache] Track {trackIndex} not cached: {ex.Message}");
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        });

        if (!_inFlight.TryAdd(key, task))
            return;
    }

    private async Task DownloadAsync(string itemId, int trackIndex, string url)
    {
        var folder = FolderFor(itemId);
        Directory.CreateDirectory(folder);

        var finalPath = FileFor(itemId, trackIndex);
        var tempPath = finalPath + ".part";

        using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _shutdown.Token)
                                        .ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug($"[StreamCache] Track {trackIndex} returned {(int)response.StatusCode}");
                return;
            }

            await using (var source = await response.Content.ReadAsStreamAsync(_shutdown.Token).ConfigureAwait(false))
            await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                                     81920, useAsync: true))
            {
                await source.CopyToAsync(target, _shutdown.Token).ConfigureAwait(false);
            }
        }

        // Rename only when complete: a half-written file must never be played.
        File.Move(tempPath, finalPath, overwrite: true);
        Trim(itemId, keep: trackIndex);

        _logger.Log($"[StreamCache] Stored track {trackIndex + 1} (index {trackIndex}) of '{itemId}' " +
                    $"({new FileInfo(finalPath).Length / 1024} KB) — next play of this track is local");
    }

    /// <summary>Keeps at most <see cref="FilesPerBook"/> files, preferring the newest track.</summary>
    private void Trim(string itemId, int keep)
    {
        try
        {
            var files = Directory.GetFiles(FolderFor(itemId), "*.audio")
                                 .OrderByDescending(f => Path.GetFileNameWithoutExtension(f))
                                 .ToList();

            var kept = 0;
            foreach (var file in files)
            {
                var index = int.TryParse(Path.GetFileNameWithoutExtension(file), out var parsed) ? parsed : -1;
                if (index == keep || kept < FilesPerBook - 1)
                {
                    if (index != keep) kept++;
                    continue;
                }

                try { File.Delete(file); } catch { /* in use */ }
            }
        }
        catch { /* trimming is best effort */ }
    }

    public void Dispose()
    {
        try { _shutdown.Cancel(); } catch { }
        _shutdown.Dispose();
        _http.Dispose();
        _downloadSlots.Dispose();
    }
}
