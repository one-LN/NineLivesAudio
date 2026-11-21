using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface ISyncService
{
    bool IsSyncing { get; }
    DateTime? LastSyncTime { get; }

    Task StartAsync();
    Task StopAsync();

    /// <summary>
    /// Re-reads <c>SyncIntervalMinutes</c> / <c>AutoSyncProgress</c> and restarts the
    /// schedule, so changing either in Settings takes effect without a restart.
    /// </summary>
    Task ApplyScheduleAsync();
    Task SyncNowAsync();
    void NotifyDataCleared();
    /// <summary>Pulls library metadata. Skipped when the same pull just happened, unless forced.</summary>
    Task SyncLibrariesAsync(bool force = false);
    Task SyncProgressAsync();

    /// <summary>Mark which item is actively playing (session owns progress).</summary>
    void SetActivePlaybackItem(string? itemId);

    /// <summary>Throttled position report during playback.</summary>
    Task ReportPlaybackPositionAsync(string itemId, double currentTime, double duration, bool isFinished = false);

    /// <summary>Force-push final progress on stop/exit.</summary>
    Task FlushPlaybackProgressAsync(string itemId, double currentTime, double duration, bool isFinished);
}

public class SyncEventArgs : EventArgs
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int ItemsSynced { get; set; }
}

public class SyncErrorEventArgs : EventArgs
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}
