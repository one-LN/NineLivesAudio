using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface IDownloadService
{
    Task<DownloadItem> QueueDownloadAsync(AudioBook audioBook);
    Task PauseDownloadAsync(string downloadId);
    Task ResumeDownloadAsync(string downloadId);
    Task CancelDownloadAsync(string downloadId);
    Task<List<DownloadItem>> GetActiveDownloadsAsync();
    Task<bool> IsBookDownloadedAsync(string audioBookId);
    Task DeleteDownloadAsync(string audioBookId);
}

public class DownloadProgressEventArgs : EventArgs
{
    public string DownloadId { get; set; } = string.Empty;
    public double Progress { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
}
