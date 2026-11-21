using NineLivesAudio.Models;
using NineLivesAudio.ViewModels;

namespace NineLivesAudio.Services;

/// <summary>
/// Pure filter/sort logic — no UI dependencies, no state, fully testable.
/// </summary>
public class LibraryFilterService : ILibraryFilterService
{
    public IReadOnlyList<AudioBook> ApplyFilters(
        IEnumerable<AudioBook> books,
        FilterOptions options,
        Func<AudioBook, NormalizedMetadata> getNormalized)
    {
        var filtered = books;

        // View mode filter
        if (options.ViewMode != ViewMode.All && !string.IsNullOrEmpty(options.SelectedGroupFilter))
        {
            filtered = options.ViewMode switch
            {
                ViewMode.Series => filtered
                    .Where(b => getNormalized(b).SeriesName == options.SelectedGroupFilter)
                    .OrderBy(b => getNormalized(b).SeriesNumber ?? double.MaxValue),
                ViewMode.Author => filtered
                    .Where(b => getNormalized(b).DisplayAuthor == options.SelectedGroupFilter),
                ViewMode.Genre => filtered
                    .Where(b => b.Genres.Contains(options.SelectedGroupFilter)),
                _ => filtered
            };
        }

        // Hide-finished filter
        if (options.HideFinished)
        {
            filtered = filtered.Where(b => !b.IsFinished && b.Progress < 1.0);
        }

        // Downloaded-only filter
        if (options.ShowDownloadedOnly)
        {
            filtered = filtered.Where(b => b.IsDownloaded);
        }

        // Search filter
        if (!string.IsNullOrWhiteSpace(options.SearchQuery))
        {
            var searchLower = options.SearchQuery.ToLowerInvariant();
            filtered = filtered.Where(b => getNormalized(b).SearchText.Contains(searchLower));
        }

        // Sort — materialize once
        return (options.SortMode switch
        {
            SortMode.Title => filtered.OrderBy(b => b.Title),
            SortMode.Author => filtered.OrderBy(b => b.Author).ThenBy(b => b.Title),
            SortMode.Progress => filtered.OrderByDescending(b => b.Progress).ThenBy(b => b.Title),
            SortMode.RecentProgress => filtered
                .OrderByDescending(b => b.Progress > 0 ? 1 : 0)
                .ThenByDescending(b => b.CurrentTime.TotalSeconds)
                .ThenBy(b => b.Title),
            _ => filtered
        }).ToList();
    }

    public IReadOnlyList<string> GetAvailableGroups(
        IEnumerable<AudioBook> books,
        ViewMode viewMode,
        Func<AudioBook, NormalizedMetadata> getNormalized)
    {
        IEnumerable<string> groups = viewMode switch
        {
            ViewMode.Series => books
                .Select(b => getNormalized(b).SeriesName)
                .Where(s => !string.IsNullOrEmpty(s))
                .Cast<string>()
                .Distinct()
                .OrderBy(s => s),
            ViewMode.Author => books
                .Select(b => getNormalized(b).DisplayAuthor)
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct()
                .OrderBy(a => a),
            ViewMode.Genre => books
                .SelectMany(b => b.Genres)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct()
                .OrderBy(g => g),
            _ => Enumerable.Empty<string>()
        };

        return groups.ToList();
    }
}
