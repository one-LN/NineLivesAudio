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

public partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly ILocalDatabase _database;
    private readonly IAudioPlaybackService _playbackService;
    private readonly ISyncService _syncService;
    private readonly ILoggingService _logger;
    private readonly IMetadataNormalizer _normalizer;
    private readonly ILocalizationService _localization;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGrid))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGrid))]
    private bool _showEmptyState;

    public bool ShowGrid => !IsLoading && !ShowEmptyState;

    [ObservableProperty]
    private string _totalListeningTimeText = "";

    public ObservableCollection<NineLivesItem> Lives { get; } = new();

    public HomeViewModel(
        ILocalDatabase database,
        IAudioPlaybackService playbackService,
        ISyncService syncService,
        ILoggingService loggingService,
        IMetadataNormalizer normalizer,
        ILocalizationService localization)
    {
        _database = database;
        _playbackService = playbackService;
        _syncService = syncService;
        _logger = loggingService;
        _normalizer = normalizer;
        _localization = localization;

        // Live progress: the list used to be a snapshot of the database taken when the page
        // loaded, so the time next to the ring stayed frozen until you left and came back.
        WeakReferenceMessenger.Default.Register<PositionChangedMessage>(this, (r, m) =>
        {
            var self = (HomeViewModel)r;

            // The player ticks several times a second; one refresh per second is enough and
            // keeps the whole list from repainting constantly.
            var now = DateTime.UtcNow;
            if ((now - self._lastLiveUpdate).TotalMilliseconds < 900) return;
            self._lastLiveUpdate = now;

            self._dispatcherQueue?.TryEnqueue(() => self.ApplyLiveProgress(m.Value));
        });

        // A position was just written (pause, stop, periodic session sync): refresh that row
        // in place. Views used to wait for the next full reload, which only happened on
        // restart for the book that was playing.
        WeakReferenceMessenger.Default.Register<PlaybackProgressSavedMessage>(this, (r, m) =>
        {
            var self = (HomeViewModel)r;
            self._dispatcherQueue?.TryEnqueue(() =>
                self.ApplyProgress(m.ItemId, m.Position, m.Duration));
        });

        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
        {
            var self = (HomeViewModel)r;

            // Stopping writes the final position to the database, so re-read it once.
            if (m.Value.State == PlaybackState.Stopped)
            {
                self._dispatcherQueue?.TryEnqueue(() => _ = self.LoadAsync());
                return;
            }

            // Started playing something that is not on the list (opened from the library):
            // reload so the row exists and starts ticking.
            if (m.Value.State == PlaybackState.Playing)
            {
                var id = self._playbackService.CurrentAudioBook?.Id;
                if (id != null && self.Lives.All(l => l.AudioBook?.Id != id))
                    self._dispatcherQueue?.TryEnqueue(() => _ = self.LoadAsync());
            }
        });

        // Reload Nine Lives whenever a sync completes (progress data may have changed).
        // A progress pull and a full sync can both report completion within a second, so
        // only reload once per second.
        WeakReferenceMessenger.Default.Register<SyncCompletedMessage>(this, (r, m) =>
        {
            var self = (HomeViewModel)r;

            var now = DateTime.UtcNow;
            if ((now - self._lastReload).TotalMilliseconds < 1000) return;
            self._lastReload = now;

            self._logger.LogDebug("[Home] Sync completed, reloading Nine Lives...");
            self._dispatcherQueue?.TryEnqueue(() => _ = self.LoadAsync());
        });
    }

    /// <summary>Throttle marker for <see cref="ApplyLiveProgress"/> (player ticks ~4×/s).</summary>
    private DateTime _lastLiveUpdate = DateTime.MinValue;

    /// <summary>Throttle marker for reloads triggered by sync messages.</summary>
    private DateTime _lastReload = DateTime.MinValue;

    /// <summary>Throttle marker for server pulls triggered by (re)entering the page.</summary>

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    /// <summary>
    /// Mirrors the player position onto the matching row: ring percentage, listened time and
    /// the "just now" stamp, and recomputes the header total.
    /// </summary>
    private void ApplyLiveProgress(TimeSpan position)
    {
        var playing = _playbackService.CurrentAudioBook;
        if (playing == null) return;

        ApplyProgress(playing.Id, position, _playbackService.Duration);
    }

    /// <summary>Writes one book's position onto its row (and the header total).</summary>
    private void ApplyProgress(string itemId, TimeSpan position, TimeSpan duration)
    {
        var item = Lives.FirstOrDefault(l => l.AudioBook?.Id == itemId);
        if (item == null) return;

        var total = duration.TotalSeconds;
        if (total <= 0) total = item.AudioBook?.Duration.TotalSeconds ?? 0;

        var elapsed = Math.Max(0, position.TotalSeconds);

        item.ProgressPercent = total > 0 ? Math.Clamp(elapsed / total * 100, 0, 100) : item.ProgressPercent;
        item.TimeGiven = CosmicCatHelper.FormatListeningTime(TimeSpan.FromSeconds(elapsed));
        item.HoursListened = elapsed / 3600.0;
        item.LastPlayedLabel = _localization.GetString("JustNow");

        TotalListeningTimeText = CosmicCatHelper.FormatListeningTime(
            TimeSpan.FromSeconds(Lives.Sum(l => l.HoursListened * 3600)));
    }

    /// <summary>
    /// Runs on every visit to the page. Progress comes back first (it is what the rings show),
    /// library metadata follows in the background so the 309-item pull never holds up the page.
    /// The old five-second throttle is gone: arriving at the page and seeing nothing change was
    /// indistinguishable from the sync being broken.
    /// </summary>
    // Never re-pull the 309-item library on every page visit.
    private DateTime _lastLibraryMetadataPull = DateTime.MinValue;

    public async Task RefreshFromServerAsync()
    {
        _lastReload = DateTime.MinValue; // allow the reload this pull produces

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _syncService.SyncProgressAsync();
            _logger.Log($"[Home] Page shown — progress synced in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Home] Page shown — progress sync failed: {ex.Message}");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Metadata is 300+ items: never while audio is loading or playing (it competes
                // for the same connection) and only if the shelf can actually have changed.
                if (_playbackService.State is PlaybackState.Playing or PlaybackState.Loading or PlaybackState.Buffering)
                {
                    _logger.LogDebug("[Home] Playback active — library metadata pull skipped");
                    return;
                }

                // Bouncing between pages must not re-download 309 items every time; progress is
                // what changes minute to minute, metadata does not.
                var sinceMetadata = DateTime.UtcNow - _lastLibraryMetadataPull;
                if (sinceMetadata.TotalSeconds < 60)
                {
                    _logger.LogDebug($"[Home] Library metadata is {sinceMetadata.TotalSeconds:F0}s old — skipping");
                    return;
                }

                _lastLibraryMetadataPull = DateTime.UtcNow;

                var libraryWatch = System.Diagnostics.Stopwatch.StartNew();
                await _syncService.SyncLibrariesAsync();
                _logger.Log($"[Home] Page shown — library metadata synced in {libraryWatch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[Home] Library metadata pull failed: {ex.Message}");
            }
        });
    }

    public async Task LoadAsync()
    {
        // Capture dispatcher queue on first call (always from UI thread)
        _dispatcherQueue ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        try
        {
            // Only the first load needs a spinner; a refresh of an already-populated shelf is
            // a silent patch, otherwise the tick would flash the page every minute.
            if (Lives.Count == 0)
                IsLoading = true;

            _logger.LogDebug("[Home] Loading Nine Lives (page shown or sync finished)...");

            var recentEntries = await _database.GetRecentlyPlayedAsync(9);

            if (HasSameOrder(recentEntries))
            {
                // Same nine books in the same order: only the numbers moved. Patch them in
                // place (the rows notify) instead of clearing and rebuilding the repeater every
                // minute — that rebuild is what made the shelf flicker during the sync tick.
                for (var i = 0; i < recentEntries.Count; i++)
                {
                    var (book, lastPlayedAt) = recentEntries[i];
                    var item = Lives[i];

                    item.AudioBook = book;
                    item.ProgressPercent = book.ProgressPercent;
                    item.IsDownloaded = book.IsDownloaded;
                    item.CoverPath = book.CoverPath;
                    item.HoursListened = book.CurrentTime.TotalHours;
                    item.TimeGiven = CosmicCatHelper.FormatListeningTime(book.CurrentTime);
                    item.LastPlayedAt = lastPlayedAt;
                    item.LastPlayedLabel = FormatRelativeTime(lastPlayedAt);
                }
            }
            else
            {
                Lives.Clear();
                bool isFirst = true;
                int idx = 0;
                foreach (var (book, lastPlayedAt) in recentEntries)
                {
                    var normalized = _normalizer.Normalize(book);
                    Lives.Add(new NineLivesItem
                    {
                        AudioBook = book,
                        DisplayTitle = normalized.DisplayTitle,
                        DisplayAuthor = normalized.DisplayAuthor,
                        ProgressPercent = book.ProgressPercent,
                        IsMostRecent = isFirst,
                        IsDownloaded = book.IsDownloaded,
                        CoverPath = book.CoverPath,
                        LifeIndex = idx,
                        LifeLabel = _localization.Format("LifeLabel", ToRoman(idx + 1)),
                        Weight = book.Duration.TotalHours < 4 ? "LIGHT"
                               : book.Duration.TotalHours < 15 ? "MEDIUM" : "HEAVY",
                        TimeGiven = CosmicCatHelper.FormatListeningTime(book.CurrentTime),
                        HoursListened = book.CurrentTime.TotalHours,
                        LastPlayedAt = lastPlayedAt,
                        LastPlayedLabel = FormatRelativeTime(lastPlayedAt)
                    });
                    isFirst = false;
                    idx++;
                }
            }

            // Compute aggregate listening time across all displayed items
            var totalTime = TimeSpan.FromSeconds(
                recentEntries.Sum(e => e.Book.CurrentTime.TotalSeconds));
            TotalListeningTimeText = CosmicCatHelper.FormatListeningTime(totalTime);

            ShowEmptyState = Lives.Count == 0;
            _logger.LogDebug($"[Home] Loaded {Lives.Count} lives, total listened: {TotalListeningTimeText}");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Home] Load failed", ex);
            ShowEmptyState = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>True when the shelf would show the same nine books in the same order.</summary>
    private bool HasSameOrder(IReadOnlyList<(AudioBook Book, DateTime LastPlayedAt)> entries)
    {
        if (entries.Count != Lives.Count) return false;

        for (var i = 0; i < entries.Count; i++)
        {
            if (!string.Equals(entries[i].Book.Id, Lives[i].AudioBook?.Id, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static readonly string[] RomanNumerals =
        { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

    private static string ToRoman(int number) =>
        number >= 1 && number <= RomanNumerals.Length
            ? RomanNumerals[number - 1]
            : number.ToString();

    private string FormatRelativeTime(DateTime timestamp)
    {
        if (timestamp == DateTime.MinValue) return "";
        var utc = timestamp.Kind == DateTimeKind.Local
            ? timestamp.ToUniversalTime()
            : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed.TotalSeconds < 0) elapsed = TimeSpan.Zero;
        if (elapsed.TotalMinutes < 1) return _localization.GetString("JustNow");
        if (elapsed.TotalMinutes < 60) return _localization.Format("MinutesAgo", (int)elapsed.TotalMinutes);
        if (elapsed.TotalHours < 24) return _localization.Format("HoursAgo", (int)elapsed.TotalHours);
        if (elapsed.TotalDays < 2) return _localization.GetString("Yesterday");
        if (elapsed.TotalDays < 7) return _localization.Format("DaysAgo", (int)elapsed.TotalDays);
        if (elapsed.TotalDays < 30) return _localization.Format("WeeksAgo", (int)(elapsed.TotalDays / 7));
        return AppLanguage.FormatShortDate(timestamp);
    }

}

/// <summary>
/// One row of the "Nine Lives" list. The progress fields are observable: they are refreshed
/// while a book is playing, which is why this class is not a plain DTO any more.
/// </summary>
public class NineLivesItem : ObservableObject
{
    public AudioBook AudioBook { get; set; } = null!;
    public string DisplayTitle { get; set; } = string.Empty;
    public string DisplayAuthor { get; set; } = string.Empty;

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    private bool _isMostRecent;
    public bool IsMostRecent
    {
        get => _isMostRecent;
        set => SetProperty(ref _isMostRecent, value);
    }

    private bool _isDownloaded;
    public bool IsDownloaded
    {
        get => _isDownloaded;
        set => SetProperty(ref _isDownloaded, value);
    }

    private double _hoursListened;
    public double HoursListened
    {
        get => _hoursListened;
        set => SetProperty(ref _hoursListened, value);
    }

    private string? _coverPath;
    public string? CoverPath
    {
        get => _coverPath;
        set => SetProperty(ref _coverPath, value);
    }

    // Altar properties
    public int LifeIndex { get; set; }
    public string LifeLabel { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;

    private string _timeGiven = string.Empty;
    public string TimeGiven
    {
        get => _timeGiven;
        set => SetProperty(ref _timeGiven, value);
    }

    // Last listened
    public DateTime LastPlayedAt { get; set; }

    private string _lastPlayedLabel = string.Empty;
    public string LastPlayedLabel
    {
        get => _lastPlayedLabel;
        set => SetProperty(ref _lastPlayedLabel, value);
    }
}
