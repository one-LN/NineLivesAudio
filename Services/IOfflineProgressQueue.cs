namespace NineLivesAudio.Services;

public interface IOfflineProgressQueue
{
    Task EnqueueAsync(string itemId, double currentTime, bool isFinished);
    Task DrainQueueAsync();
    Task<int> GetPendingCountAsync();
}
