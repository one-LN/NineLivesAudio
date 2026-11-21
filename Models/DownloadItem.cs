namespace NineLivesAudio.Models;

public class DownloadItem
{
    public string Id { get; set; } = string.Empty;
    public string AudioBookId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; }
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public double Progress => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> FilesToDownload { get; set; } = new();
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;

    public string SizeDisplay => TotalBytes switch
    {
        > 1_073_741_824 => $"{TotalBytes / 1_073_741_824.0:F1} GB",
        > 1_048_576 => $"{TotalBytes / 1_048_576.0:F1} MB",
        > 1024 => $"{TotalBytes / 1024.0:F1} KB",
        _ => $"{TotalBytes} B"
    };
}

public enum DownloadStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}
