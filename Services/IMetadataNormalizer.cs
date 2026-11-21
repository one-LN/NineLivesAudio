using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface IMetadataNormalizer
{
    /// <summary>
    /// Normalizes raw audiobook metadata for cleaner display.
    /// Does NOT modify the original AudioBook object.
    /// </summary>
    NormalizedMetadata Normalize(AudioBook raw);
}

/// <summary>
/// Cleaned metadata for display. Never written back to server.
/// </summary>
public class NormalizedMetadata
{
    /// <summary>Cleaned title (removed [Unabridged], (Audiobook), etc.)</summary>
    public string DisplayTitle { get; set; } = string.Empty;

    /// <summary>Original title for fallback</summary>
    public string RawTitle { get; set; } = string.Empty;

    /// <summary>Cleaned author (First Last format, multiple authors joined)</summary>
    public string DisplayAuthor { get; set; } = string.Empty;

    /// <summary>Individual authors as list</summary>
    public List<string> AuthorList { get; set; } = new();

    /// <summary>Series name (extracted from title if missing)</summary>
    public string? SeriesName { get; set; }

    /// <summary>Series number as decimal (supports "1.5", "2", etc.)</summary>
    public double? SeriesNumber { get; set; }

    /// <summary>Formatted series display: "Series Name #3" or null</summary>
    public string? DisplaySeries { get; set; }

    /// <summary>Cleaned narrator (removed "Read by", "Narrated by")</summary>
    public string? DisplayNarrator { get; set; }

    /// <summary>Search-optimized text (title + author + series, lowercase)</summary>
    public string SearchText { get; set; } = string.Empty;
}
