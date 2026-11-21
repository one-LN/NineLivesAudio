using NineLivesAudio.Models;
using NineLivesAudio.ViewModels;

namespace NineLivesAudio.Services;

/// <summary>
/// Pure filter/sort logic extracted from LibraryViewModel.
/// Stateless — all inputs passed as parameters.
/// </summary>
public interface ILibraryFilterService
{
    /// <summary>
    /// Applies view mode, search, sort, and toggle filters to produce a sorted result list.
    /// </summary>
    IReadOnlyList<AudioBook> ApplyFilters(
        IEnumerable<AudioBook> books,
        FilterOptions options,
        Func<AudioBook, NormalizedMetadata> getNormalized);

    /// <summary>
    /// Extracts distinct group names for a given view mode (series, author, genre).
    /// </summary>
    IReadOnlyList<string> GetAvailableGroups(
        IEnumerable<AudioBook> books,
        ViewMode viewMode,
        Func<AudioBook, NormalizedMetadata> getNormalized);
}

/// <summary>
/// Immutable filter options passed to ApplyFilters.
/// </summary>
public sealed class FilterOptions
{
    public ViewMode ViewMode { get; init; } = ViewMode.All;
    public SortMode SortMode { get; init; } = SortMode.Default;
    public string? SelectedGroupFilter { get; init; }
    public string? SearchQuery { get; init; }
    public bool HideFinished { get; init; }
    public bool ShowDownloadedOnly { get; init; }
}
