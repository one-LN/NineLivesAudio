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

    // Marshals background messages (connectivity pings) onto the UI thread.
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue =
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    // True while "downloaded only" is active because the server was unreachable.
    // The flag lets us switch the filter back off once the server answers again,
    // without fighting a filter the user turned on by hand.
    private bool _autoOfflineFallback;
    private bool _settingOfflineFallback;
    private bool _sessionExpiredNotified;

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

    // Bulk-replaceable: a refresh swaps hundreds of books with one Reset notification
    // instead of one CollectionChanged per book (the ItemsRepeater re-lays out once).
    public RangeObservableCollection<AudioBook> AudioBooks { get; } = new();
    public RangeObservableCollection<AudioBook> FilteredAudioBooks { get; } = new();
    public ObservableCollection<string> AvailableGroups { get; } = new();

    // A page load, a library switch and a reconnect can all ask for a refresh at the
    // same moment. Running them one after another meant the shelf was rebuilt two or
    // three times (and the spinner stayed up through every network round-trip).
    private Task? _booksLoadInFlight;
    private Task? _librariesLoadInFlight;

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

        // When the server becomes reachable again, reload instead of leaving the
        // user staring at a cached/empty shelf until they hit refresh.
        WeakReferenceMessenger.Default.Register<ConnectivityChangedMessage>(this, (r, m) =>
            ((LibraryViewModel)r).OnConnectivityChanged(m.Value));

        // A full sync (first connect, "Sync now", 5-minute tick) can bring in new libraries
        // and progress. Reload afterwards so the shelf stops showing the pre-connect state.
        WeakReferenceMessenger.Default.Register<SyncCompletedMessage>(this, (r, m) =>
            ((LibraryViewModel)r).OnSyncCompleted());
    }

    private void OnConnectivityChanged(ConnectivityChangedEventArgs e)
    {
        if (!e.IsOnline || !e.IsServerReachable) return;

        var dispatcher = _dispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => _ = ReloadAfterReconnectAsync());
            return;
        }

        _ = ReloadAfterReconnectAsync();
    }

    private void OnSyncCompleted()
    {
        var dispatcher = _dispatcherQueue;
        if (dispatcher != null && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => _ = ReloadAfterSyncAsync());
            return;
        }

        _ = ReloadAfterSyncAsync();
    }

    /// <summary>Refresh the shelf after a completed sync (no extra sync is triggered here).</summary>
    private async Task ReloadAfterSyncAsync()
    {
        if (IsLoading) return;

        try
        {
            await LoadLibrariesAsync();
            if (SelectedLibrary != null)
                await LoadAudioBooksAsync();
            _logger.Log("[Library] Shelf refreshed after sync");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Library] Post-sync refresh failed", ex);
        }
    }

    private async Task ReloadAfterReconnectAsync()
    {
        if (IsLoading) return;

        _logger.Log("[Library] Server reachable again — refreshing library");
        try
        {
            await LoadLibrariesAsync();
            if (SelectedLibrary != null)
                await LoadAudioBooksAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("[Library] Reconnect refresh failed", ex);
        }
    }

    /// <summary>
    /// Shows only downloaded books as a resilience fallback and remembers that we
    /// (not the user) switched the filter, so it can be undone automatically.
    /// </summary>
    private void ApplyOfflineFallback()
    {
        if (!ShowDownloadedOnly)
        {
            _settingOfflineFallback = true;
            try
            {
                ShowDownloadedOnly = true;
            }
            finally
            {
                _settingOfflineFallback = false;
            }
        }

        _autoOfflineFallback = true;
    }

    private void ClearOfflineFallback()
    {
        if (!_autoOfflineFallback) return;
        _autoOfflineFallback = false;

        if (ShowDownloadedOnly)
            ShowDownloadedOnly = false;
    }

    /// <summary>
    /// Turns a silent 401 into an explicit "session expired" state: the in-memory
    /// session is dropped, the empty state switches to "not connected" and the user
    /// is told once to sign in again — instead of an empty library with no hint.
    /// </summary>
    private void HandleUnauthorizedIfNeeded()
    {
        if (!_apiService.LastRequestUnauthorized) return;

        _apiService.InvalidateSession();

        if (_sessionExpiredNotified) return;
        _sessionExpiredNotified = true;

        var message = _localization.GetString("SessionExpired");
        ErrorMessage = message;
        HasError = true;
        _notifications.ShowWarning(message, _localization.GetString("NotConnected"));
        _logger.LogWarning("[Library] Token rejected with 401 — session invalidated");
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
    private Task LoadLibrariesAsync()
    {
        if (_librariesLoadInFlight is { IsCompleted: false })
            return _librariesLoadInFlight;

        _librariesLoadInFlight = LoadLibrariesCoreAsync();
        return _librariesLoadInFlight;
    }

    private async Task LoadLibrariesCoreAsync()
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

                        // A successful answer proves the session works again.
                        _sessionExpiredNotified = false;
                    }
                    else
                    {
                        HandleUnauthorizedIfNeeded();
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

    /// <summary>True when both lists hold the same books in the same order (id-only compare).</summary>
    private static bool HasSameBooks(IReadOnlyList<AudioBook> current, IReadOnlyList<AudioBook> candidate)
    {
        if (current.Count != candidate.Count) return false;

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].Id, candidate[i].Id, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    [RelayCommand]
    private Task LoadAudioBooksAsync()
    {
        if (_booksLoadInFlight is { IsCompleted: false })
        {
            _logger.LogDebug("[Library] Books load already running — joining it");
            return _booksLoadInFlight;
        }

        _booksLoadInFlight = LoadAudioBooksCoreAsync();
        return _booksLoadInFlight;
    }

    private async Task LoadAudioBooksCoreAsync()
    {
        // Never bail out silently: pick a library when one exists, otherwise show
        // the empty state so the user sees *why* the shelf is blank.
        if (SelectedLibrary == null && Libraries.Count > 0)
            SelectedLibrary = Libraries[0];

        if (SelectedLibrary == null)
        {
            _logger.LogWarning("LoadAudioBooksAsync called but no library is available");
            UpdateAvailableGroups();
            ApplyFilterInternal();
            UpdateShowEmptyState();
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
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var localBooks = await _database.GetAudioBooksByLibraryAsync(SelectedLibrary.Id);
            _logger.Log($"[Library] Local load: {localBooks.Count} books in {stopwatch.ElapsedMilliseconds} ms");

            AudioBooks.ReplaceRange(localBooks);
            UpdateAvailableGroups();
            ApplyFilterInternal();

            // Stop the spinner here: from now on the shelf is usable and everything
            // below is a background refresh. An empty cache keeps the spinner up so the
            // first paint is not a "no books" empty state.
            if (AudioBooks.Count > 0)
                IsLoading = false;

            // If online, delegate to SyncService for server refresh + merge.
            // SyncService handles LocalPath preservation with robust matching.
            if (_apiService.IsAuthenticated)
            {
                try
                {
                    _logger.Log("Triggering SyncService for fresh server data...");
                    var syncStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    await _syncService.SyncLibrariesAsync();
                    _logger.Log($"[Library] Server sync finished in {syncStopwatch.ElapsedMilliseconds} ms");

                    // Reload from DB after sync (SyncService saved the merged data).
                    // Skipped when the ids did not change: re-binding 309 cards costs more
                    // than the query itself.
                    var refreshedBooks = await _database.GetAudioBooksByLibraryAsync(SelectedLibrary.Id);
                    if (!HasSameBooks(AudioBooks, refreshedBooks))
                    {
                        AudioBooks.ReplaceRange(refreshedBooks);
                        UpdateAvailableGroups();
                        _logger.Log($"[Library] Server data changed — {AudioBooks.Count} books re-bound");
                    }
                    else
                    {
                        _logger.LogDebug("[Library] Server data unchanged — keeping the current list");
                    }

                    // The server answered: drop the offline-only filter again (unless the
                    // user picked it themselves) and refresh the empty state accordingly.
                    var wasFallback = _autoOfflineFallback;
                    ClearOfflineFallback();
                    ApplyFilterInternal();

                    if (wasFallback)
                        _notifications.ShowSuccess(_localization.GetString("BackOnline"));

                    HandleUnauthorizedIfNeeded();
                }
                catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or OperationCanceledException)
                {
                    _logger.LogWarning($"[Library] Server unreachable, using cached books: {ex.Message}");
                    // Continue with locally cached books already loaded above
                    ApplyOfflineFallback();
                    _notifications.ShowInfo(_localization.GetString("ShowingDownloadedOnly"));
                }
            }

            IsLoading = false;
            _logger.Log($"[Library] Load completed in {stopwatch.ElapsedMilliseconds} ms. " +
                        $"Total books: {AudioBooks.Count}, Filtered: {FilteredAudioBooks.Count}");
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
        // A user-initiated toggle means the offline fallback is no longer ours to undo.
        if (!_settingOfflineFallback)
            _autoOfflineFallback = false;

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

        FilteredAudioBooks.ReplaceRange(newItems);
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
