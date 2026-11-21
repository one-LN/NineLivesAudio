namespace NineLivesAudio.Models;

public class UserProgress
{
    public string LibraryItemId { get; set; } = string.Empty;
    public string? EpisodeId { get; set; }
    public TimeSpan CurrentTime { get; set; }
    public double Progress { get; set; }
    public bool IsFinished { get; set; }
    public DateTime? LastUpdate { get; set; }
}
