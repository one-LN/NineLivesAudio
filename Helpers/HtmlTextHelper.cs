using System.Net;
using System.Text.RegularExpressions;

namespace NineLivesAudio.Helpers;

/// <summary>
/// Turns the HTML that Audiobookshelf stores in a book's description
/// (<c>&lt;p&gt;</c>, <c>&lt;b&gt;</c>, <c>&lt;a&gt;</c>, <c>&amp;gt;</c> entities, …)
/// into plain text a <c>TextBlock</c> can render. Block-level tags become line
/// breaks, every other tag is dropped, and HTML entities are decoded last so
/// escaped markup (e.g. <c>&amp;lt;p&amp;gt;</c>) stays visible as literal text.
/// </summary>
public static class HtmlTextHelper
{
    private static readonly Regex BrRegex = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BlockCloseRegex = new(@"</(p|div|li|h[1-6]|tr|blockquote)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BlockOpenRegex = new(@"<(p|div|li|h[1-6]|blockquote)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex TrailingSpaceRegex = new(@"[ \t]+\n", RegexOptions.Compiled);
    private static readonly Regex MultiBlankRegex = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>Returns the plain-text rendering of <paramref name="html"/>, or an empty string.</summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = BrRegex.Replace(html, "\n");
        text = BlockCloseRegex.Replace(text, "\n");
        text = BlockOpenRegex.Replace(text, "\n");
        text = TagRegex.Replace(text, string.Empty);

        // Decode entities only after the tags are gone, so "&lt;p&gt;" in the
        // source stays literal text instead of becoming a real tag.
        text = WebUtility.HtmlDecode(text);

        // Non-breaking spaces render as unbreakable gaps in TextBlock; a normal
        // space wraps the same way a browser would lay the text out.
        text = text.Replace('\u00A0', ' ');

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = TrailingSpaceRegex.Replace(text, "\n");
        text = MultiBlankRegex.Replace(text, "\n\n");
        return text.Trim();
    }
}
