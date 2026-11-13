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
    private bool _isLoading;

    [ObservableProperty]
    private bool _showEmptyState;

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

        // Reload Nine Lives whenever a sync completes (progress data may have changed)
        WeakReferenceMessenger.Default.Register<SyncCompletedMessage>(this, (r, m) =>
        {
            var self = (HomeViewModel)r;
            self._logger.Log("[Home] Sync completed, reloading Nine Lives...");
            self._dispatcherQueue?.TryEnqueue(() => _ = self.LoadAsync());
        });
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    public async Task LoadAsync()
    {
        // Capture dispatcher queue on first call (always from UI thread)
        _dispatcherQueue ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        try
        {
            IsLoading = true;
            _logger.Log("[Home] Loading Nine Lives...");

            var recentEntries = await _database.GetRecentlyPlayedAsync(9);

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

            // Compute aggregate listening time across all displayed items
            var totalTime = TimeSpan.FromSeconds(
                recentEntries.Sum(e => e.Book.CurrentTime.TotalSeconds));
            TotalListeningTimeText = CosmicCatHelper.FormatListeningTime(totalTime);

            ShowEmptyState = Lives.Count == 0;
            _logger.Log($"[Home] Loaded {Lives.Count} lives, total listened: {TotalListeningTimeText}");
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

    private static readonly string[] RomanNumerals =
        { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

    private static string ToRoman(int number) =>
        number >= 1 && number <= RomanNumerals.Length
            ? RomanNumerals[number - 1]
            : number.ToString();

    private string FormatRelativeTime(DateTime timestamp)
    {
        if (timestamp == DateTime.MinValue) return "";
        var elapsed = DateTime.Now - timestamp;
        if (elapsed.TotalMinutes < 1) return _localization.GetString("JustNow");
        if (elapsed.TotalMinutes < 60) return _localization.Format("MinutesAgo", (int)elapsed.TotalMinutes);
        if (elapsed.TotalHours < 24) return _localization.Format("HoursAgo", (int)elapsed.TotalHours);
        if (elapsed.TotalDays < 2) return _localization.GetString("Yesterday");
        if (elapsed.TotalDays < 7) return _localization.Format("DaysAgo", (int)elapsed.TotalDays);
        if (elapsed.TotalDays < 30) return _localization.Format("WeeksAgo", (int)(elapsed.TotalDays / 7));
        return AppLanguage.FormatShortDate(timestamp);
    }

    [RelayCommand]
    private async Task PlayBookAsync(NineLivesItem? item)
    {
        if (item?.AudioBook == null) return;

        try
        {
            _logger.Log($"[Home] Resuming: {item.DisplayTitle}");
            var loaded = await _playbackService.LoadAudioBookAsync(item.AudioBook);
            if (loaded)
            {
                await _playbackService.PlayAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("[Home] Play failed", ex);
        }
    }
}

public class NineLivesItem
{
    public AudioBook AudioBook { get; set; } = null!;
    public string DisplayTitle { get; set; } = string.Empty;
    public string DisplayAuthor { get; set; } = string.Empty;
    public double ProgressPercent { get; set; }
    public bool IsMostRecent { get; set; }
    public bool IsDownloaded { get; set; }
    public double HoursListened { get; set; }
    public string? CoverPath { get; set; }

    // Altar properties
    public int LifeIndex { get; set; }
    public string LifeLabel { get; set; } = string.Empty;
    public string Weight { get; set; } = string.Empty;
    public string TimeGiven { get; set; } = string.Empty;

    // Last listened
    public DateTime LastPlayedAt { get; set; }
    public string LastPlayedLabel { get; set; } = string.Empty;
}
