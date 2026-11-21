using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Data;
using NineLivesAudio.Messages;

namespace NineLivesAudio.Services;

public class BookmarkQueue : IBookmarkQueue
{
    private readonly ILocalDatabase _database;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly IConnectivityService _connectivity;
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _drainLock = new(1, 1);

    private int _pendingCount;

    public BookmarkQueue(
        ILocalDatabase database,
        IAudioBookshelfApiService apiService,
        IConnectivityService connectivity,
        ILoggingService logger)
    {
        _database = database;
        _apiService = apiService;
        _connectivity = connectivity;
        _logger = logger;

        // Same pattern as the progress queue: drain as soon as the server is reachable again.
        WeakReferenceMessenger.Default.Register<ConnectivityChangedMessage>(this, async (r, m) =>
        {
            if (m.Value.IsServerReachable)
                await ((BookmarkQueue)r).DrainAsync();
        });

        _ = RefreshCountAsync();
    }

    public int PendingCount => _pendingCount;

    public async Task<BookmarkCreateResult> CreateAsync(string itemId, string title, double time)
    {
        if (string.IsNullOrEmpty(itemId))
            return BookmarkCreateResult.Failed;

        if (_apiService.IsAuthenticated && _connectivity.IsServerReachable)
        {
            try
            {
                if (await _apiService.CreateBookmarkAsync(itemId, title, time))
                {
                    _logger.Log($"[Bookmarks] Created: '{title}' at {time:F1}s");
                    return BookmarkCreateResult.Created;
                }

                _logger.LogWarning($"[Bookmarks] Server rejected bookmark '{title}' at {time:F1}s");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Bookmarks] Create failed, queueing locally: {ex.Message}");
            }
        }

        try
        {
            await _database.EnqueuePendingBookmarkAsync(itemId, title, time);
            await RefreshCountAsync();
            _logger.Log($"[Bookmarks] Queued offline: '{title}' at {time:F1}s (item {itemId}, {_pendingCount} waiting)");
            return BookmarkCreateResult.Queued;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Bookmarks] Could not queue bookmark", ex);
            return BookmarkCreateResult.Failed;
        }
    }

    public async Task DrainAsync()
    {
        if (!_apiService.IsAuthenticated || !_connectivity.IsServerReachable)
            return;

        if (!await _drainLock.WaitAsync(0))
            return;

        try
        {
            var pending = await _database.GetPendingBookmarksAsync();
            if (pending.Count == 0)
            {
                await RefreshCountAsync();
                return;
            }

            var sent = 0;
            foreach (var bookmark in pending)
            {
                // Stop at the first failure: the rest stay queued for the next attempt.
                if (!await _apiService.CreateBookmarkAsync(bookmark.ItemId, bookmark.Title, bookmark.Time))
                    break;

                await _database.DeletePendingBookmarkAsync(bookmark.Id);
                sent++;
            }

            await RefreshCountAsync();

            if (sent > 0)
                _logger.Log($"[Bookmarks] Flushed {sent} queued bookmark(s), {_pendingCount} still waiting");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Bookmarks] Drain failed: {ex.Message}");
        }
        finally
        {
            _drainLock.Release();
        }
    }

    private async Task RefreshCountAsync()
    {
        try
        {
            var pending = await _database.GetPendingBookmarksAsync();
            _pendingCount = pending.Count;
        }
        catch
        {
            // Count is informational only.
        }
    }
}
