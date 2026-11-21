namespace NineLivesAudio.Models;

/// <summary>A bookmark created while the server was unreachable, waiting to be uploaded.</summary>
public class PendingBookmarkEntry
{
    public long Id { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double Time { get; set; }
    public DateTime Timestamp { get; set; }
}
