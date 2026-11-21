using Microsoft.UI.Xaml.Media;

namespace NineLivesAudio.Models;

/// <summary>
/// View model for displaying an audio track/file in the book detail track list.
/// </summary>
public class TrackItem
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public bool IsDownloaded { get; set; }

    // Same reason as ChapterDisplayItem: the XamlReader template needs brushes, not #hex literals.
    public Brush? TitleBrush { get; set; }
    public Brush? MetaBrush { get; set; }
    public Brush? RowBackgroundBrush { get; set; }
}
