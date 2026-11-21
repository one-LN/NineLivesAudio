using NineLivesAudio.Models;
using System.Text.RegularExpressions;

namespace NineLivesAudio.Services;

public partial class MetadataNormalizer : IMetadataNormalizer
{
    // ═══════════════════════════════════════════════════════════════════════
    // COMPILED REGEX PATTERNS (for performance)
    // ═══════════════════════════════════════════════════════════════════════

    // Title cleanup patterns
    [GeneratedRegex(@"\s*[\[\(](?:Unabridged|Abridged|Audiobook|Audio(?:\s*Book)?|Narrated|Full Cast)[\]\)]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TitleNoisePattern();

    [GeneratedRegex(@"\s*-\s*(?:Unabridged|Abridged|Audiobook)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleSuffixPattern();

    // Series extraction from title: "Series Name #3", "Series (Book 3)", "Book 3: Title"
    [GeneratedRegex(@"^(.+?)\s*#(\d+(?:\.\d+)?)\s*(?:[-:]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesHashPattern();

    [GeneratedRegex(@"^(.+?)\s*[\(\[]Book\s*(\d+(?:\.\d+)?)[\)\]]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesBookParenPattern();

    [GeneratedRegex(@"^(.+?),?\s*Book\s*(\d+(?:\.\d+)?)\s*(?:[-:,]|of|$)", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesBookCommaPattern();

    // Author cleanup: "Last, First" format
    [GeneratedRegex(@"^([^,]+),\s*(.+)$")]
    private static partial Regex AuthorLastFirstPattern();

    // Author separators
    [GeneratedRegex(@"\s*[;&]\s*|\s+and\s+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorSeparatorPattern();

    // Narrator cleanup
    [GeneratedRegex(@"^(?:Read|Narrated|Performed)\s+by\s+", RegexOptions.IgnoreCase)]
    private static partial Regex NarratorPrefixPattern();

    // Whitespace normalization
    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpacePattern();

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═══════════════════════════════════════════════════════════════════════

    public NormalizedMetadata Normalize(AudioBook raw)
    {
        var result = new NormalizedMetadata
        {
            RawTitle = raw.Title ?? string.Empty
        };

        // Normalize title
        result.DisplayTitle = NormalizeTitle(raw.Title);

        // Normalize author
        var (displayAuthor, authorList) = NormalizeAuthor(raw.Author);
        result.DisplayAuthor = displayAuthor;
        result.AuthorList = authorList;

        // Normalize series (use existing or extract from title)
        var (seriesName, seriesNumber) = NormalizeSeries(raw.SeriesName, raw.SeriesSequence, raw.Title);
        result.SeriesName = seriesName;
        result.SeriesNumber = seriesNumber;
        result.DisplaySeries = FormatDisplaySeries(seriesName, seriesNumber);

        // Normalize narrator
        result.DisplayNarrator = NormalizeNarrator(raw.Narrator);

        // Build search text
        result.SearchText = BuildSearchText(result);

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TITLE NORMALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var result = title;

        // Remove noise patterns: [Unabridged], (Audiobook), etc.
        result = TitleNoisePattern().Replace(result, " ");

        // Remove trailing suffixes: " - Unabridged"
        result = TitleSuffixPattern().Replace(result, "");

        // Normalize whitespace
        result = MultiSpacePattern().Replace(result, " ").Trim();

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AUTHOR NORMALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    private static (string display, List<string> list) NormalizeAuthor(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
            return (string.Empty, new List<string>());

        // Split by common separators: ; & and
        var parts = AuthorSeparatorPattern().Split(author);
        var normalized = new List<string>();

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Check for "Last, First" format
            var match = AuthorLastFirstPattern().Match(trimmed);
            if (match.Success)
            {
                var first = match.Groups[2].Value.Trim();
                var last = match.Groups[1].Value.Trim();
                trimmed = $"{first} {last}";
            }

            // Normalize whitespace
            trimmed = MultiSpacePattern().Replace(trimmed, " ");

            // Dedupe (case-insensitive)
            if (!normalized.Any(a => a.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                normalized.Add(trimmed);
        }

        var display = string.Join(", ", normalized);
        return (display, normalized);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SERIES NORMALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    private static (string? name, double? number) NormalizeSeries(string? seriesName, string? seriesSequence, string? title)
    {
        // If we have explicit series info, use it
        if (!string.IsNullOrWhiteSpace(seriesName))
        {
            double? num = null;
            if (!string.IsNullOrWhiteSpace(seriesSequence) &&
                double.TryParse(seriesSequence.Trim(), out var parsed))
            {
                num = parsed;
            }
            return (seriesName.Trim(), num);
        }

        // Try to extract from title
        if (string.IsNullOrWhiteSpace(title))
            return (null, null);

        // Pattern: "Series Name #3"
        var hashMatch = SeriesHashPattern().Match(title);
        if (hashMatch.Success)
        {
            var name = hashMatch.Groups[1].Value.Trim();
            if (double.TryParse(hashMatch.Groups[2].Value, out var num))
                return (name, num);
        }

        // Pattern: "Series (Book 3)" or "Series [Book 3]"
        var parenMatch = SeriesBookParenPattern().Match(title);
        if (parenMatch.Success)
        {
            var name = parenMatch.Groups[1].Value.Trim();
            if (double.TryParse(parenMatch.Groups[2].Value, out var num))
                return (name, num);
        }

        // Pattern: "Series, Book 3: Title" or "Series Book 3 -"
        var commaMatch = SeriesBookCommaPattern().Match(title);
        if (commaMatch.Success)
        {
            var name = commaMatch.Groups[1].Value.Trim();
            if (double.TryParse(commaMatch.Groups[2].Value, out var num))
                return (name, num);
        }

        return (null, null);
    }

    private static string? FormatDisplaySeries(string? name, double? number)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        if (number.HasValue)
        {
            // Format: "Series Name #3" or "Series Name #1.5"
            var numStr = number.Value % 1 == 0
                ? ((int)number.Value).ToString()
                : number.Value.ToString("0.#");
            return $"{name} #{numStr}";
        }

        return name;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NARRATOR NORMALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    private static string? NormalizeNarrator(string? narrator)
    {
        if (string.IsNullOrWhiteSpace(narrator))
            return null;

        var result = narrator.Trim();

        // Remove "Read by", "Narrated by", "Performed by" prefixes
        result = NarratorPrefixPattern().Replace(result, "");

        // Normalize whitespace
        result = MultiSpacePattern().Replace(result, " ").Trim();

        return string.IsNullOrEmpty(result) ? null : result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SEARCH TEXT
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildSearchText(NormalizedMetadata meta)
    {
        var parts = new List<string>
        {
            meta.DisplayTitle,
            meta.RawTitle,
            meta.DisplayAuthor,
            meta.SeriesName ?? string.Empty,
            meta.DisplayNarrator ?? string.Empty
        };

        // Join and lowercase for case-insensitive search
        return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p))).ToLowerInvariant();
    }
}
