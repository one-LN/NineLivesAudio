using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Data;
using NineLivesAudio.Helpers;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using System.Collections.ObjectModel;

namespace NineLivesAudio.ViewModels;

public enum ViewMode
{
    All,
    Series,
    Author,
    Genre
}

public enum SortMode
{
    Default,
    Title,
    Author,
    Progress,
    RecentProgress
}

public partial class LibraryViewModel : ObservableObject, IDisposable
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILocalDatabase _database;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IDownloadService _downloadService;
    private readonly ISyncService _syncService;
    private readonly ILoggingService _logger;
    private readonly INotificationService _notifications;
    private readonly IMetadataNormalizer _normalizer;
    private readonly ILibraryFilterService _filterService;
    private readonly ILocalizationService _localization;

    // Debounce timer for search
    private CancellationTokenSource? _searchDebounceToken;
    private const int SearchDebounceMs = 300;

    // Cache for normalized metadata (avoid recomputing on every render)
    private readonly Dictionary<string, NormalizedMetadata> _normalizedCache = new();
    private const int NormalizedCacheMaxSize = 500;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private Library? _selectedLibrary;

    [ObservableProperty]
    private AudioBook? _selectedAudioBook;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _showEmptyState;

    [ObservableProperty]
    private string _emptyStateTitle = string.Empty; // localized in ctor

    [ObservableProperty]
    private string _emptyStateSubtitle = string.Empty; // localized in ctor

    [ObservableProperty]
    private ViewMode _currentViewMode = ViewMode.All;

    [ObservableProperty]
    private string? _selectedGroupFilter;

    [ObservableProperty]
    private SortMode _currentSortMode = SortMode.Default;

    [ObservableProperty]
    private bool _hideFinished;

    [ObservableProperty]
    private bool _showDownloadedOnly;

    public ObservableCollection<Library> Libraries { get; } = new();
    public ObservableCollection<AudioBook> AudioBooks { get; } = new();
    public ObservableCollection<AudioBook> FilteredAudioBooks { get; } = new();
    public ObservableCollection<string> AvailableGroups { get; } = new();

    public LibraryViewModel(
        IAudioBookshelfApiService apiService,
        ILocalDatabase database,
        IAudioPlaybackService playbackService,
        IDownloadService downloadService,
        ISyncService syncService,
        ILoggingService logger,
        INotificationService notifications,
        IMetadataNormalizer normalizer,
        ILibraryFilterService filterService,
        ILocalizationService localization)
    {
        _apiService = apiService;
        _database = database;
        _playbackService = playbackService;
        _downloadService = downloadService;
        _syncService = syncService;
        _logger = logger;
        _notifications = notifications;
        _normalizer = normalizer;
        _filterService = filterService;
        _localization = localization;

        // Seed localized defaults (field initializers cannot use the service).
        EmptyStateTitle = _localization.GetString("NoBooksFound");
        EmptyStateSubtitle = _localization.GetString("NotConnectedSubtitle");

        // Subscribe to download events for toasts
        WeakReferenceMessenger.Default.Register<DownloadCompletedMessage>(this, (r, m) =>
            ((LibraryViewModel)r).OnDownloadCompleted(m.Value));
        WeakReferenceMessenger.Default.Register<DownloadFailedMessage>(this, (r, m) =>
            ((LibraryViewModel)r).OnDownloadFailed(m.Value));
    }

    /// <summary>
    /// Gets normalized metadata for an audiobook (cached).
    /// </summary>
    public NormalizedMetadata GetNormalized(AudioBook book)
    {
        if (!_normalizedCache.TryGetValue(book.Id, out var cached))
        {
            // Evict entire cache if it exceeds max size to prevent unbounded growth
            if (_normalizedCache.Count >= NormalizedCacheMaxSize)
                _normalizedCache.Clear();

            cached = _normalizer.Normalize(book);
            _normalizedCache[book.Id] = cached;
        }
        return cached;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Try to restore auth state if not already authenticated
            if (!_apiService.IsAuthenticated)
            {
                _logger.Log("API not authenticated, attempting to validate saved token...");
                var validated = await _apiService.ValidateTokenAsync();
                _logger.Log($"Token validation result: {validated}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to validate token", ex);
        }

        await LoadLibrariesAsync();
    }

    public void Dispose()
    {
        _searchDebounceToken?.Cancel();
        _searchDebounceToken?.Dispose();
        _searchDebounceToken = null;

        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        try
        {
            _logger.Log("LoadLibrariesAsync started");
            IsLoading = true;
            HasError = false;

            // Try to load from local database first
            _logger.Log("Loading libraries from local database...");
            var localLibraries = await _database.GetAllLibrariesAsync();
            _logger.Log($"Loaded {localLibraries.Count} libraries from database");

            Libraries.Clear();
            foreach (var library in localLibraries)
            {
                Libraries.Add(library);
            }

            // If online, refresh from server
            if (_apiService.IsAuthenticated)
            {
                try
                {
                    _logger.Log("Fetching libraries from server...");
                    var serverLibraries = await _apiService.GetLibrariesAsync();
                    _logger.Log($"Received {serverLibraries.Count} libraries from server");

                    if (serverLibraries.Any())
                    {
                        await _database.SaveLibrariesAsync(serverLibraries);

                        Libraries.Clear();
                        foreach (var library in serverLibraries)
                        {
                            Libraries.Add(library);
                        }
                    }
                }
                catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or OperationCanceledException)
                {
                    _logger.LogWarning($"[Library] Server unreachable, using cached libraries: {ex.Message}");
                    // Continue with locally cached libraries already loaded above
                }
            }

            // Select first library if none selected
            if (SelectedLibrary == null && Libraries.Any())
            {
                _logger.Log($"Auto-selecting first library: {Libraries.First().Name}");
                SelectedLibrary = Libraries.First();
            }

            _logger.Log("LoadLibrariesAsync completed");
        }
        catch (Exception ex)
        {
            _logger.LogError("LoadLibrariesAsync failed", ex);
            ErrorMessage = _localization.Format("FailedLoadLibraries", ex.Message);
            HasError = true;
            _notifications.ShowError(ex.Message, _localization.GetString("LoadFailed"));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadAudioBooksAsync()
    {
        if (SelectedLibrary == null)
        {
            _logger.LogWarning("LoadAudioBooksAsync called but no library selected");
            return;
        }

        try
        {
            _logger.Log($"LoadAudioBooksAsync started for library: {SelectedLibrary.Name}");
            IsLoading = true;
            HasError = false;

            // Clear normalized cache when loading fresh data
            _normalizedCache.Clear();

            // Load from local database first (filtered by selected library)
            _logger.Log("Loading audiobooks from local database...");
            var localBooks = await _database.GetAudioBooksByLibraryAsync(SelectedLibrary.Id);
            _logger.Log($"Loaded {localBooks.Count} audiobooks from database");

            AudioBooks.Clear();
            foreach (var book in localBooks)
            {
                AudioBooks.Add(book);
            }

            // If online, delegate to SyncService for server refresh + merge.
            // SyncService handles LocalPath preservation with robust matching.
            if (_apiService.IsAuthenticated)
            {
                try
                {
                    _logger.Log("Triggering SyncService for fresh server data...");
                    await _syncService.SyncLibrariesAsync();

                    // Reload from DB after sync (SyncService saved the merged data)
                    var refreshedBooks = await _database.GetAudioBooksByLibraryAsync(SelectedLibrary.Id);
                    AudioBooks.Clear();
                    foreach (var book in refreshedBooks)
                        AudioBooks.Add(book);
                    _logger.Log($"Reloaded {AudioBooks.Count} audiobooks from database after sync");
                }
                catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or OperationCanceledException)
                {
                    _logger.LogWarning($"[Library] Server unreachable, using cached books: {ex.Message}");
                    // Continue with locally cached books already loaded above
                    ShowDownloadedOnly = true;
                    _notifications.ShowInfo(_localization.GetString("ShowingDownloadedOnly"));
                }
            }

            _logger.Log("Updating available groups...");
            UpdateAvailableGroups();
            _logger.Log("Applying filter...");
            ApplyFilterInternal();
            _logger.Log($"LoadAudioBooksAsync completed. Total books: {AudioBooks.Count}, Filtered: {FilteredAudioBooks.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"LoadAudioBooksAsync failed for library {SelectedLibrary?.Name}", ex);
            ErrorMessage = _localization.Format("FailedLoadBooks", ex.Message);
            HasError = true;
        }
        finally
        {
            IsLoading = false;
            UpdateShowEmptyState();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await _syncService.SyncNowAsync();
        await LoadLibrariesAsync();
        if (SelectedLibrary != null)
        {
            await LoadAudioBooksAsync();
        }
        IsRefreshing = false;
        _notifications.ShowSuccess(_localization.GetString("LibraryRefreshed"));
    }

    [RelayCommand]
    private async Task PlayAudioBookAsync(AudioBook? audioBook)
    {
        if (audioBook == null) return;

        try
        {
            var loaded = await _playbackService.LoadAudioBookAsync(audioBook);
            if (loaded)
            {
                await _playbackService.PlayAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("FailedPlay", ex.Message);
            HasError = true;
            _notifications.ShowError(ex.Message, _localization.GetString("PlaybackError"));
        }
    }

    [RelayCommand]
    private async Task DownloadAudioBookAsync(AudioBook? audioBook)
    {
        if (audioBook == null) return;

        try
        {
            await _downloadService.QueueDownloadAsync(audioBook);
            _notifications.ShowInfo(_localization.Format("DownloadingTitle", audioBook.Title));
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("FailedQueue", ex.Message);
            HasError = true;
            _notifications.ShowError(ex.Message, _localization.GetString("DownloadError"));
        }
    }

    [RelayCommand]
    private async Task RemoveDownloadAsync(AudioBook? audioBook)
    {
        if (audioBook == null) return;

        try
        {
            await _downloadService.DeleteDownloadAsync(audioBook.Id);
            audioBook.IsDownloaded = false;
            audioBook.LocalPath = null;
            foreach (var af in audioBook.AudioFiles) af.LocalPath = null;
            _normalizedCache.Remove(audioBook.Id);
            _notifications.ShowSuccess(_localization.Format("DownloadRemoved", audioBook.Title));
        }
        catch (Exception ex)
        {
            _notifications.ShowError(ex.Message, _localization.GetString("RemoveDownloadError"));
        }
    }

    private void OnDownloadCompleted(DownloadItem item)
    {
        _notifications.ShowSuccess(_localization.Format("DownloadComplete", item.Title));

        // Update the AudioBook in our collection
        var book = AudioBooks.FirstOrDefault(b => b.Id == item.AudioBookId);
        if (book != null)
        {
            book.IsDownloaded = true;
            // Clear cache to force re-render
            _normalizedCache.Remove(book.Id);
        }
    }

    private void OnDownloadFailed(DownloadItem item)
    {
        _notifications.ShowError($"{item.Title}: {item.ErrorMessage}", _localization.GetString("DownloadFailed"));
    }

    partial void OnSelectedLibraryChanged(Library? value)
    {
        if (value != null)
        {
            _ = LoadAudioBooksAsync();
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Debounce search to reduce filtering churn (async/await, no ContinueWith).
        _searchDebounceToken?.Cancel();
        _searchDebounceToken?.Dispose();
        _searchDebounceToken = new CancellationTokenSource();
        var token = _searchDebounceToken.Token;
        _ = DebounceApplyFilterAsync(token);
    }

    private async Task DebounceApplyFilterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, token);
            if (!token.IsCancellationRequested)
                ApplyFilterInternal();
        }
        catch (OperationCanceledException) { /* superseded by newer keystroke */ }
    }

    partial void OnCurrentViewModeChanged(ViewMode value)
    {
        UpdateAvailableGroups();
        SelectedGroupFilter = null;
        ApplyFilterInternal();
    }

    partial void OnSelectedGroupFilterChanged(string? value)
    {
        ApplyFilterInternal();
    }

    partial void OnCurrentSortModeChanged(SortMode value)
    {
        ApplyFilterInternal();
    }

    partial void OnHideFinishedChanged(bool value)
    {
        ApplyFilterInternal();
    }

    partial void OnShowDownloadedOnlyChanged(bool value)
    {
        ApplyFilterInternal();
    }

    [RelayCommand]
    private void SetViewMode(ViewMode mode)
    {
        CurrentViewMode = mode;
    }

    private void UpdateAvailableGroups()
    {
        AvailableGroups.Clear();
        var groups = _filterService.GetAvailableGroups(AudioBooks, CurrentViewMode, GetNormalized);
        foreach (var group in groups)
        {
            AvailableGroups.Add(group);
        }
    }

    private void ApplyFilterInternal()
    {
        try
        {
            _logger.LogDebug($"ApplyFilter called. SearchQuery: '{SearchQuery}', ViewMode: {CurrentViewMode}, AudioBooks count: {AudioBooks.Count}");

            var options = new FilterOptions
            {
                ViewMode = CurrentViewMode,
                SortMode = CurrentSortMode,
                SelectedGroupFilter = SelectedGroupFilter,
                SearchQuery = SearchQuery,
                HideFinished = HideFinished,
                ShowDownloadedOnly = ShowDownloadedOnly
            };

            var results = _filterService.ApplyFilters(AudioBooks, options, GetNormalized);
            ReplaceFilteredCollection(results);

            UpdateShowEmptyState();
            _logger.LogDebug($"ApplyFilter completed. Filtered count: {FilteredAudioBooks.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError("ApplyFilter failed", ex);
        }
    }

    /// <summary>
    /// Replaces the contents of FilteredAudioBooks with minimal notifications.
    /// Avoids the N+1 notification storm of Clear + individual Add calls.
    /// </summary>
    private void ReplaceFilteredCollection(IReadOnlyList<AudioBook> newItems)
    {
        // Short-circuit: if contents are identical, skip entirely
        if (FilteredAudioBooks.Count == newItems.Count)
        {
            bool same = true;
            for (int i = 0; i < newItems.Count; i++)
            {
                if (!ReferenceEquals(FilteredAudioBooks[i], newItems[i]))
                {
                    same = false;
                    break;
                }
            }
            if (same) return;
        }

        FilteredAudioBooks.Clear();
        foreach (var book in newItems)
        {
            FilteredAudioBooks.Add(book);
        }
    }

    /// <summary>Re-renders computed strings after the UI language changed.</summary>
    public void RefreshLocalizedStrings() => UpdateShowEmptyState();

    private void UpdateShowEmptyState()
    {
        ShowEmptyState = !IsLoading && FilteredAudioBooks.Count == 0;

        if (ShowEmptyState)
        {
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                EmptyStateTitle = _localization.GetString("NoResults");
                EmptyStateSubtitle = _localization.Format("NoResultsSubtitle", SearchQuery);
            }
            else if (!_apiService.IsAuthenticated)
            {
                EmptyStateTitle = _localization.GetString("NotConnected");
                EmptyStateSubtitle = _localization.GetString("NotConnectedSubtitle");
            }
            else if (AudioBooks.Count == 0)
            {
                EmptyStateTitle = _localization.GetString("LibraryEmpty");
                EmptyStateSubtitle = _localization.GetString("LibraryEmptySubtitle");
            }
            else
            {
                EmptyStateTitle = _localization.GetString("NoBooksFound");
                EmptyStateSubtitle = _localization.GetString("TryChangingFilters");
            }
        }
    }

    // --- Grouping logic (extracted from LibraryPage code-behind) ---

    /// <summary>
    /// Creates group items for the current view mode (Series, Author, or Genre).
    /// </summary>
    public IReadOnlyList<LibraryGroupItem> CreateGroups()
    {
        return CurrentViewMode switch
        {
            ViewMode.Series => CreateSeriesGroups(),
            ViewMode.Author => CreateAuthorGroups(),
            ViewMode.Genre => CreateGenreGroups(),
            _ => Array.Empty<LibraryGroupItem>()
        };
    }

    /// <summary>
    /// Gets the books belonging to a specific group, ordered appropriately.
    /// </summary>
    public IReadOnlyList<AudioBook> GetBooksForGroup(string groupName)
    {
        return CurrentViewMode switch
        {
            ViewMode.Series => AudioBooks
                .Where(b => b.SeriesName == groupName)
                .OrderBy(b => SeriesHelper.ParseSequence(b.SeriesSequence))
                .ToList(),
            ViewMode.Author => AudioBooks
                .Where(b => b.Author == groupName)
                .OrderBy(b => b.Title)
                .ToList(),
            ViewMode.Genre => AudioBooks
                .Where(b => b.Genres.Contains(groupName))
                .OrderBy(b => b.Title)
                .ToList(),
            _ => Array.Empty<AudioBook>()
        };
    }

    private List<LibraryGroupItem> CreateSeriesGroups()
    {
        return AudioBooks
            .Where(b => !string.IsNullOrEmpty(b.SeriesName))
            .GroupBy(b => b.SeriesName!)
            .Select(g => new LibraryGroupItem
            {
                Name = g.Key,
                BookCount = g.Count(),
                CoverUrl = g.OrderBy(b => SeriesHelper.ParseSequence(b.SeriesSequence)).FirstOrDefault()?.CoverPath,
                Icon = "\uE8F1"
            })
            .OrderBy(g => g.Name)
            .ToList();
    }

    private List<LibraryGroupItem> CreateAuthorGroups()
    {
        return AudioBooks
            .Where(b => !string.IsNullOrEmpty(b.Author))
            .GroupBy(b => b.Author)
            .Select(g => new LibraryGroupItem
            {
                Name = g.Key,
                BookCount = g.Count(),
                CoverUrl = g.FirstOrDefault()?.CoverPath,
                Icon = "\uE77B"
            })
            .OrderBy(g => g.Name)
            .ToList();
    }

    private List<LibraryGroupItem> CreateGenreGroups()
    {
        return AudioBooks
            .SelectMany(b => b.Genres.Select(g => new { Genre = g, Book = b }))
            .GroupBy(x => x.Genre)
            .Select(g => new LibraryGroupItem
            {
                Name = g.Key,
                BookCount = g.Count(),
                CoverUrl = g.FirstOrDefault()?.Book.CoverPath,
                Icon = "\uE8D6"
            })
            .OrderBy(g => g.Name)
            .ToList();
    }
}
