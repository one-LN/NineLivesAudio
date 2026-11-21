namespace NineLivesAudio.Models;

/// <summary>
/// Represents a grouping of audiobooks (by series, author, or genre) for library display.
/// </summary>
public class LibraryGroupItem
{
    public string Name { get; set; } = string.Empty;
    public int BookCount { get; set; }
    public string? CoverUrl { get; set; }
    public string Icon { get; set; } = "\uE8F1";
    public string BookCountText => BookCount == 1 ? "1 book" : $"{BookCount} books";
}
