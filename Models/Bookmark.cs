namespace NineLivesAudio.Models;

public class Bookmark
{
    public string Id { get; set; } = string.Empty;
    public string LibraryItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double Time { get; set; }  // seconds
    public DateTime CreatedAt { get; set; }

    /// <summary>Formatted time for display (HH:MM:SS or MM:SS).</summary>
    public string TimeFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(Time);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}
