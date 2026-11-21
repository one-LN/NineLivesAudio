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
    private readonly ISettingsService _settingsService;
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
        ILocalizationService localization,
        ISettingsService settingsService)
    {
        _apiService = apiService;
        _database = database;
        _playbackService = playbackService;
        _settingsService = settingsService;
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

        // Positions are persisted on pause/stop/every 12s. Patch the shelf's own objects so
        // their progress text moves without waiting for a refresh.
        WeakReferenceMessenger.Default.Register<PlaybackProgressSavedMessage>(this, (r, m) =>
        {
            var self = (LibraryViewModel)r;
            self._dispatcherQueue?.TryEnqueue(() =>
            {
                var book = self.AudioBooks.FirstOrDefault(b => b.Id == m.ItemId);
                if (book == null) return;

                book.CurrentTime = m.Position;
                if (m.Duration.TotalSeconds > 0)
                {
                    var ratio = m.Position.TotalSeconds / m.Duration.TotalSeconds;
                    book.Progress = book.Progress > 1.0 ? ratio * 100 : ratio;
                }
                if (m.IsFinished) book.IsFinished = true;
            });
        });

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

    /// <summary>
    /// Refresh the shelf after a completed sync. This is a background refresh: it reads the
    /// database (the sync already wrote it) and patches the existing objects, so no spinner
    /// appears, the list is not reset and scrolling is not lost. The timer fires every minute,
    /// so anything visible here would flicker constantly.
    /// </summary>
    private async Task ReloadAfterSyncAsync()
    {
        if (IsLoading) return;

        var now = DateTime.UtcNow;
        if ((now - _lastBackgroundRefreshAt).TotalMilliseconds < 1000) return;
        _lastBackgroundRefreshAt = now;

        // Nothing on screen yet (first run / new library): fall back to the visible path.
        if (SelectedLibrary == null)
        {
            await LoadLibrariesAsync();
            return;
        }

        try
        {
            var books = await _database.GetAudioBooksByLibraryAsync(SelectedLibrary.Id);
            if (books.Count == 0 && !_apiService.IsAuthenticated)
            {
                Libraries.Clear();
                SelectedLibrary = null;
                AudioBooks.Clear();
                FilteredAudioBooks.Clear();
                _normalizedCache.Clear();
                _logger.Log("[Library] Cleared in-memory shelf after server disconnect/delete");
                return;
            }
            MergeBooks(books);
            UpdateShowEmptyState();
            _logger.LogDebug($"[Library] Background refresh merged {books.Count} books (idle, no re-render)");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Library] Post-sync refresh failed", ex);
        }
    }

    /// <summary>
    /// Runs VM work on the UI thread. Touching an <see cref="ObservableCollection{T}"/> or even a
    /// bound property from a background thread makes WinRT marshal a notification across threads
    /// — which throws (and that is exactly what surfaced as "load failed").
    /// </summary>
    private Task RunOnUiThreadAsync(Func<Task> work)
    {
        var dispatcher = _dispatcherQueue;
        if (dispatcher == null || dispatcher.HasThreadAccess)
            return work();

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var queued = dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await work();
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        if (!queued) completion.TrySetResult(false);

        return completion.Task;
    }

    private async Task ReloadAfterReconnectAsync()
    {
        if (IsLoading) return;

        // Coming back online used to start a full pull immediately — a 12-second stall in the
        // log, while audio was playing. Patch from the database first (instant, silent), then
        // let the expensive pull happen once the connection has settled.
        _logger.Log("[Library] Server reachable again — patching from cache, full pull in 45s");

        // Deliberately NOT clearing _lastServerRefreshAt here: it used to make the very next page
        // refresh pull all 309 items again, so a reconnect cost two 12-second pulls back to back
        // (14:22:40 and 14:23:27 in app_2026-11-8.log). The pull below forces itself instead.

        try
        {
            await ReloadAfterSyncAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("[Library] Reconnect cache patch failed", ex);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(45));

                // Waiting is fine on a worker thread; touching the collections is not.
                await RunOnUiThreadAsync(async () =>
                {
                    if (IsLoading) return;

                    // Never spend the shelf's bandwidth on metadata while something is playing.
                    if (_playbackService.State is PlaybackState.Playing or PlaybackState.Loading or PlaybackState.Buffering)
                    {
                        _logger.LogDebug("[Library] Playback active — delayed full pull skipped");
                        return;
                    }

                    await LoadLibrariesAsync();
                    if (SelectedLibrary != null)
                        _lastServerRefreshAt = DateTime.MinValue; // force the fetch below

                    await _syncService.SyncLibrariesAsync(force: true);
                    await LoadAudioBooksAsync();

                    // Just fetched: let the throttle hold off the next page refresh.
                    _lastServerRefreshAt = DateTime.UtcNow;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Library] Delayed reconnect refresh failed ({ex.GetType().Name})", ex);
            }
        });
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

        // Re-entering the page (the Frame hands back the cached instance) used to download the
        // library list again and clear/refill the collections, which is the "always loading"
        // the shelf showed while the periodic sync was running.
        if (Libraries.Count > 0 && (DateTime.UtcNow - _lastLibraryListLoadAt).TotalSeconds < 30)
        {
            _logger.LogDebug("[Library] Page re-entered — shelf already current, no reload");
            return;
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

    private Task LoadLibrariesAsync()
    {
        if (_dispatcherQueue is { HasThreadAccess: false })
            return RunOnUiThreadAsync(LoadLibrariesAsync);

        if (_librariesLoadInFlight is { IsCompleted: false })
            return _librariesLoadInFlight;

        _librariesLoadInFlight = LoadLibrariesCoreAsync();
        return _librariesLoadInFlight;
    }

    // When the library list was last fetched (page entry throttle, see InitializeAsync).
    private DateTime _lastLibraryListLoadAt = DateTime.MinValue;

    // The log showed three "Triggering SyncService" lines inside 300 ms because concurrent
    // loads all passed the 30-second check before any of them set it. Only one may win.
    private int _serverRefreshGate;

    // Post-sync refreshes arrive in bursts (progress message + full-sync message); one per
    // second is plenty for a background patch.
    private DateTime _lastBackgroundRefreshAt = DateTime.MinValue;

    private async Task LoadLibrariesCoreAsync()
    {
        try
        {
            _lastLibraryListLoadAt = DateTime.UtcNow;
            _logger.LogDebug("LoadLibrariesAsync started");
            IsLoading = true;
            HasError = false;

            // Try to load from local database first
            _logger.LogDebug("Loading libraries from local database...");
            var localLibraries = await _database.GetAllLibrariesAsync();
            _logger.LogDebug($"Loaded {localLibraries.Count} libraries from database");

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
                    _logger.LogDebug("Fetching libraries from server...");
                    var serverLibraries = await _apiService.GetLibrariesAsync();
                    _logger.LogDebug($"Received {serverLibraries.Count} libraries from server");

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
                _logger.LogDebug($"Auto-selecting first library: {Libraries.First().Name}");
                SelectedLibrary = Libraries.First();
            }

            _logger.LogDebug("LoadLibrariesAsync completed");
        }
        catch (Exception ex)
        {
            _logger.LogError($"LoadLibrariesAsync failed ({ex.GetType().Name})", ex);

            // WinRT's own messages are empty strings; showing "Failed: " tells the user nothing.
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            ErrorMessage = _localization.Format("FailedLoadLibraries", detail);
            HasError = true;
            _notifications.ShowError(detail, _localization.GetString("LoadFailed"));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Replaces the shelf only when the set or order of books actually changed. Otherwise the
    /// incoming values are copied onto the existing objects, which raise change notifications —
    /// no ItemsRepeater reset, so a background refresh no longer blanks or scrolls the shelf.
    /// </summary>
    private void MergeBooks(IReadOnlyList<AudioBook> incoming)
    {
        if (!HasSameBooks(AudioBooks, incoming))
        {
            AudioBooks.ReplaceRange(incoming);
            return;
        }

        for (var i = 0; i < incoming.Count; i++)
            CopyProgress(AudioBooks[i], incoming[i]);
    }

    private static void CopyProgress(AudioBook target, AudioBook source)
    {
        target.CurrentTime = source.CurrentTime;
        target.Progress = source.Progress;
        target.IsFinished = source.IsFinished;

        if (target.IsDownloaded != source.IsDownloaded)
            target.IsDownloaded = source.IsDownloaded;
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

    private Task LoadAudioBooksAsync()
    {
        if (_dispatcherQueue is { HasThreadAccess: false })
            return RunOnUiThreadAsync(LoadAudioBooksAsync);

        if (_booksLoadInFlight is { IsCompleted: false })
        {
            _logger.LogDebug("[Library] Books load already running — joining it");
            return _booksLoadInFlight;
        }

        _booksLoadInFlight = LoadAudioBooksCoreAsync();
        return _booksLoadInFlight;
    }

    // Set when the page last pulled a fresh library from the server. Re-entering the page
    // (a cached instance re-raises Loaded) must not trigger another full download.
    private DateTime _lastServerRefreshAt = DateTime.MinValue;

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
            _logger.LogDebug($"LoadAudioBooksAsync started for library: {SelectedLibrary.Name}");

            // A spinner only makes sense when there is nothing on screen. Re-entering the page
            // with a full shelf used to blank the list and show the spinner again.
            if (AudioBooks.Count == 0)
                IsLoading = true;

            HasError = false;

            // Clear normalized cache when loading fresh data
            _normalizedCache.Clear();

            // Load from local database first (filtered by selected library)
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var localBooks = await _database.GetAudioBooksByLibraryAsync(SelectedLibrary.Id);
            _logger.Log($"[Library] Local load: {localBooks.Count} books in {stopwatch.ElapsedMilliseconds} ms");

            MergeBooks(localBooks);
            UpdateAvailableGroups();
            ApplyFilterInternal();

            // Stop the spinner here: from now on the shelf is usable and everything
            // below is a background refresh. An empty cache keeps the spinner up so the
            // first paint is not a "no books" empty state.
            if (AudioBooks.Count > 0)
                IsLoading = false;

            // If online, delegate to SyncService for server refresh + merge.
            // SyncService handles LocalPath preservation with robust matching.
            // Skipped when the periodic sync covered it recently — the shelf is already current
            // and re-downloading every time the page is shown is what made it "always loading".
            // Ten minutes while audio is playing: a full metadata pull is 300+ items and four
            // parallel requests, and competing with the stream is exactly the "it stutters
            // while the shelf refreshes" the user reported. Idle pages keep the 30 s refresh.
            var refreshThrottle = _playbackService.State is PlaybackState.Playing or PlaybackState.Loading
                ? TimeSpan.FromMinutes(10)
                : TimeSpan.FromSeconds(30);

            var sinceRefresh = DateTime.UtcNow - _lastServerRefreshAt;
            var mayRefresh = _apiService.IsAuthenticated
                             && sinceRefresh >= refreshThrottle
                             && System.Threading.Interlocked.CompareExchange(ref _serverRefreshGate, 1, 0) == 0;

            if (mayRefresh)
            {
                _lastServerRefreshAt = DateTime.UtcNow;
                try
                {
                    _logger.LogDebug("Triggering SyncService for fresh server data...");
                    var syncStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    await _syncService.SyncLibrariesAsync();
                    _logger.LogDebug($"[Library] Server sync finished in {syncStopwatch.ElapsedMilliseconds} ms");

                    // Reload from DB after sync (SyncService saved the merged data).
                    // Skipped when the ids did not change: re-binding 309 cards costs more
                    // than the query itself.
                    var refreshedBooks = await _database.GetAudioBooksByLibraryAsync(SelectedLibrary.Id);
                    if (!HasSameBooks(AudioBooks, refreshedBooks))
                    {
                        MergeBooks(refreshedBooks);
                        UpdateAvailableGroups();
                        _logger.Log($"[Library] Server data changed — {AudioBooks.Count} books re-bound");
                    }
                    else
                    {
                        // Same books, same order: only the progress moved. Update the existing
                        // objects (they notify) instead of rebinding the shelf.
                        MergeBooks(refreshedBooks);
                        _logger.LogDebug("[Library] Server data unchanged — progress patched in place");
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
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _serverRefreshGate, 0);
                }
            }

            IsLoading = false;
            _logger.Log($"[Library] Load completed in {stopwatch.ElapsedMilliseconds} ms. " +
                        $"Total books: {AudioBooks.Count}, Filtered: {FilteredAudioBooks.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"LoadAudioBooksAsync failed for library {SelectedLibrary?.Name} ({ex.GetType().Name})", ex);

            var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            ErrorMessage = _localization.Format("FailedLoadBooks", detail);
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.Log("[Library] Manual refresh requested");

        // Explicit user action: pull the library too, not just progress, and allow the reload
        // below to hit the server even though the background throttle said "recently synced".
        await _syncService.SyncLibrariesAsync(force: true);
        await _syncService.SyncProgressAsync();
        _lastServerRefreshAt = DateTime.MinValue;
        await LoadLibrariesAsync();
        if (SelectedLibrary != null)
        {
            await LoadAudioBooksAsync();
        }
        IsRefreshing = false;
        _logger.Log($"[Library] Manual refresh finished in {stopwatch.ElapsedMilliseconds} ms ({AudioBooks.Count} books)");
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

    // Set while applying saved preferences, so restoring does not immediately write them back.
    private bool _restoringLibraryPreferences;

    /// <summary>
    /// Sort order, the two filters and the layout were reset on every launch. They are the kind
    /// of setting a user sets once, so they are now stored alongside the rest of the app settings.
    /// </summary>
    private void RestoreLibraryPreferences()
    {
        _restoringLibraryPreferences = true;
        try
        {
            var settings = _settingsService.Settings;

            if (Enum.TryParse<SortMode>(settings.LibrarySortMode, out var sort))
                CurrentSortMode = sort;
            if (Enum.TryParse<ViewMode>(settings.LibraryViewMode, out var view))
                CurrentViewMode = view;

            HideFinished = settings.LibraryHideFinished;
            ShowDownloadedOnly = settings.LibraryShowDownloadedOnly;
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Library] Could not restore view preferences: {ex.Message}");
        }
        finally
        {
            _restoringLibraryPreferences = false;
        }
    }

    private void PersistLibraryPreferences()
    {
        if (_restoringLibraryPreferences) return;

        try
        {
            var settings = _settingsService.Settings;
            settings.LibrarySortMode = CurrentSortMode.ToString();
            settings.LibraryViewMode = CurrentViewMode.ToString();
            settings.LibraryHideFinished = HideFinished;
            settings.LibraryShowDownloadedOnly = ShowDownloadedOnly;

            _ = _settingsService.SaveSettingsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Library] Could not save view preferences: {ex.Message}");
        }
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
        PersistLibraryPreferences();
    }

    partial void OnHideFinishedChanged(bool value)
    {
        ApplyFilterInternal();
        PersistLibraryPreferences();
    }

    partial void OnShowDownloadedOnlyChanged(bool value)
    {
        // A user-initiated toggle means the offline fallback is no longer ours to undo.
        if (!_settingOfflineFallback)
            _autoOfflineFallback = false;

        ApplyFilterInternal();
        PersistLibraryPreferences();
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
