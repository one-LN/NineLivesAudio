using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Data;
using NineLivesAudio.Messages;

namespace NineLivesAudio.Services;

public class OfflineProgressQueue : IOfflineProgressQueue
{
    private readonly ILocalDatabase _database;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly IConnectivityService _connectivity;
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _drainLock = new(1, 1);

    public OfflineProgressQueue(
        ILocalDatabase database,
        IAudioBookshelfApiService apiService,
        IConnectivityService connectivity,
        ILoggingService logger)
    {
        _database = database;
        _apiService = apiService;
        _connectivity = connectivity;
        _logger = logger;

        // Auto-drain when connectivity returns
        WeakReferenceMessenger.Default.Register<ConnectivityChangedMessage>(this, async (r, m) =>
        {
            if (m.Value.IsServerReachable)
            {
                ((OfflineProgressQueue)r)._logger.Log("[OfflineQueue] Server reachable, draining queue");
                await ((OfflineProgressQueue)r).DrainQueueAsync();
            }
        });
    }

    public async Task EnqueueAsync(string itemId, double currentTime, bool isFinished)
    {
        await _database.EnqueuePendingProgressAsync(itemId, currentTime, isFinished);
        _logger.LogDebug($"[OfflineQueue] Enqueued: {itemId} @ {currentTime:F1}s");
    }

    public async Task DrainQueueAsync()
    {
        if (!_apiService.IsAuthenticated || !_connectivity.IsServerReachable)
            return;

        if (!await _drainLock.WaitAsync(0))
            return;

        try
        {
            var pending = await _database.GetPendingProgressAsync();
            if (pending.Count == 0) return;

            // Deduplicate: keep only the latest entry per item
            var latest = pending
                .GroupBy(p => p.ItemId)
                .Select(g => g.OrderByDescending(p => p.Timestamp).First())
                .ToList();

            int synced = 0;
            int skipped = 0;
            var syncedItemIds = new List<string>();
            foreach (var entry in latest)
            {
                try
                {
                    // Check if server already has a newer position (e.g., from another device)
                    var serverProgress = await _apiService.GetUserProgressAsync(entry.ItemId);
                    if (serverProgress != null && serverProgress.LastUpdate > entry.Timestamp)
                    {
                        _logger.Log($"[OfflineQueue] Skipping {entry.ItemId}: server is newer ({serverProgress.LastUpdate:O} > {entry.Timestamp:O})");
                        syncedItemIds.Add(entry.ItemId); // mark as handled — don't retry stale data
                        skipped++;
                        continue;
                    }

                    // Pending entries only store the position; read the book so the
                    // server receives a correct 0..1 ratio instead of guessing.
                    double duration = 0;
                    try
                    {
                        var book = await _database.GetAudioBookAsync(entry.ItemId);
                        duration = book?.Duration.TotalSeconds ?? 0;
                    }
                    catch { /* duration is optional */ }

                    await _apiService.UpdateProgressAsync(entry.ItemId, entry.CurrentTime, entry.IsFinished, duration);
                    synced++;
                    syncedItemIds.Add(entry.ItemId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[OfflineQueue] Sync failed for {entry.ItemId}: {ex.Message}");
                }
            }

            // Only clear entries that were successfully synced
            if (syncedItemIds.Count == latest.Count)
                await _database.ClearPendingProgressAsync();
            else if (syncedItemIds.Count > 0)
                await _database.ClearPendingProgressForItemsAsync(syncedItemIds);

            _logger.Log($"[OfflineQueue] Drained {synced}/{latest.Count} entries (skipped {skipped} with newer server data)");
        }
        finally
        {
            _drainLock.Release();
        }
    }

    public async Task<int> GetPendingCountAsync()
    {
        return await _database.GetPendingProgressCountAsync();
    }
}
