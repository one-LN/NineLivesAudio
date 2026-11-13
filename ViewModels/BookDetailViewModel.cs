using NineLivesAudio.Data;
using NineLivesAudio.Helpers;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace NineLivesAudio.ViewModels;

/// <summary>
/// ViewModel for BookDetailPage — manages download state, play initiation,
/// and chapter/track data preparation.
/// </summary>
public partial class BookDetailViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlaybackService _playbackService;
    private readonly IDownloadService _downloadService;
    private readonly ILocalDatabase _database;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private AudioBook? _audioBook;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _downloadProgressPercent;

    [ObservableProperty]
    private string _downloadStatusText = string.Empty;

    [ObservableProperty]
    private bool _isPlayLoading;

    [ObservableProperty]
    private string _playButtonText = "Play"; // localized on init

    [ObservableProperty]
    private string _downloadButtonText = "Download"; // localized on init

    [ObservableProperty]
    private bool _showChapterProgress;

    private bool _isSubscribedToDownloadEvents;

    public BookDetailViewModel(
        IAudioPlaybackService playbackService,
        IDownloadService downloadService,
        ILocalDatabase database,
        ILoggingService logger,
        ILocalizationService localization)
    {
        _playbackService = playbackService;
        _downloadService = downloadService;
        _database = database;
        _logger = logger;
        _localization = localization;
    }

    /// <summary>
    /// Initializes the ViewModel with the audiobook to display.
    /// </summary>
    public void Initialize(AudioBook audioBook)
    {
        AudioBook = audioBook;
        IsDownloaded = audioBook.IsDownloaded;
        UpdateDownloadButtonText();
        PlayButtonText = audioBook.ProgressPercent > 0 ? _localization.GetString("Continue") : _localization.GetString("Play");
    }

    /// <summary>
    /// Gets the formatted duration string for the audiobook.
    /// </summary>
    public string FormattedDuration => AudioBook != null
        ? TimeFormatHelper.FormatDuration(AudioBook.Duration)
        : string.Empty;

    /// <summary>
    /// Gets chapter display items for the current audiobook.
    /// </summary>
    public IReadOnlyList<ChapterDisplayItem> GetChapterDisplayItems()
    {
        if (AudioBook?.Chapters == null || AudioBook.Chapters.Count == 0)
            return Array.Empty<ChapterDisplayItem>();

        var currentIdx = AudioBook.GetCurrentChapterIndex();
        var currentTimeSeconds = AudioBook.CurrentTime.TotalSeconds;

        return AudioBook.Chapters.Select((ch, idx) =>
        {
            double progressPercent = 0;
            string status;
            if (currentTimeSeconds >= ch.End)
            {
                progressPercent = 100;
                status = "done";
            }
            else if (idx == currentIdx && currentTimeSeconds >= ch.Start)
            {
                progressPercent = Math.Clamp(
                    (currentTimeSeconds - ch.Start) / (ch.End - ch.Start) * 100.0, 0, 100);
                status = "current";
            }
            else
            {
                status = "upcoming";
            }

            return new ChapterDisplayItem
            {
                Index = idx + 1,
                Title = ch.Title,
                StartTime = TimeFormatHelper.FormatDuration(ch.StartTime),
                Duration = TimeFormatHelper.FormatDuration(ch.Duration),
                IsCurrent = idx == currentIdx,
                ProgressPercent = progressPercent,
                Status = status
            };
        }).ToList();
    }

    /// <summary>
    /// Gets track display items for the current audiobook.
    /// </summary>
    public IReadOnlyList<TrackItem> GetTrackDisplayItems()
    {
        if (AudioBook?.AudioFiles == null || AudioBook.AudioFiles.Count == 0)
            return Array.Empty<TrackItem>();

        return AudioBook.AudioFiles.Select((af, idx) => new TrackItem
        {
            Index = idx + 1,
            Title = af.Filename,
            Duration = TimeFormatHelper.FormatDuration(af.Duration),
            IsDownloaded = !string.IsNullOrEmpty(af.LocalPath)
        }).ToList();
    }

    /// <summary>
    /// Computes the progress display info (text + percent) for book or chapter mode.
    /// </summary>
    public (string progressText, double progressPercent, string? chapterInfo) GetProgressInfo()
    {
        if (AudioBook == null)
            return (_localization.GetString("NotStarted"), 0, null);

        var pct = AudioBook.ProgressPercent;

        if (ShowChapterProgress && AudioBook.Chapters.Count > 0 && pct > 0)
        {
            var chIdx = AudioBook.GetCurrentChapterIndex();
            var chapter = AudioBook.GetCurrentChapter();
            var chPct = AudioBook.CurrentChapterProgressPercent;

            if (chapter != null && chIdx >= 0)
            {
                var chapterDisplay = _localization.Format("ChapterOf", chIdx + 1, AudioBook.Chapters.Count, chapter.Title);
                var elapsed = TimeFormatHelper.FormatDuration(
                    TimeSpan.FromSeconds(AudioBook.CurrentTime.TotalSeconds - chapter.Start));
                var chDur = TimeFormatHelper.FormatDuration(chapter.Duration);
                var chInfo = $"{(int)chPct}% of chapter ({elapsed} / {chDur})";
                return (chapterDisplay, chPct, chInfo);
            }
        }

        if (pct > 0)
        {
            var currentPosition = TimeFormatHelper.FormatDuration(AudioBook.CurrentTime);
            var totalDuration = TimeFormatHelper.FormatDuration(AudioBook.Duration);
            return ($"{(int)pct}% complete ({currentPosition} / {totalDuration})", pct, null);
        }

        return (_localization.GetString("NotStarted"), 0, null);
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (AudioBook == null) return;

        try
        {
            IsPlayLoading = true;
            PlayButtonText = _localization.GetString("Loading");

            // Refresh audiobook from DB to ensure IsDownloaded + AudioFile.LocalPath are current
            var freshBook = await _database.GetAudioBookAsync(AudioBook.Id);
            if (freshBook != null)
                AudioBook = freshBook;

            var loaded = await _playbackService.LoadAudioBookAsync(AudioBook);
            if (loaded)
            {
                await _playbackService.PlayAsync();
                _logger.Log("Playback started from book detail");
            }
            else
            {
                _logger.LogWarning("Failed to load audiobook for playback");
                PlayButtonText = _localization.GetString("FailedTryAgain");
                await Task.Delay(3000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error playing audiobook", ex);
            PlayButtonText = _localization.Format("ErrorLabel", ex.Message);
            await Task.Delay(3000);
        }
        finally
        {
            IsPlayLoading = false;
            PlayButtonText = AudioBook?.ProgressPercent > 0 ? _localization.GetString("Continue") : _localization.GetString("Play");
        }
    }

    [RelayCommand]
    private async Task ToggleDownloadAsync()
    {
        if (AudioBook == null) return;

        try
        {
            if (IsDownloaded)
            {
                DownloadButtonText = _localization.GetString("Downloading");
                await _downloadService.DeleteDownloadAsync(AudioBook.Id);
                AudioBook.IsDownloaded = false;
                AudioBook.LocalPath = null;
                foreach (var af in AudioBook.AudioFiles) af.LocalPath = null;
                IsDownloaded = false;
                _logger.Log($"Removed download for: {AudioBook.Title}");
            }
            else
            {
                DownloadButtonText = _localization.GetString("Downloading");
                _logger.Log($"Starting download for: {AudioBook.Title}, AudioFiles: {AudioBook.AudioFiles.Count}");

                SubscribeToDownloadEvents();

                var downloadItem = await _downloadService.QueueDownloadAsync(AudioBook);
                IsDownloading = true;
                DownloadButtonText = _localization.GetString("Downloading");
                _logger.Log($"Queued download for: {AudioBook.Title}, DownloadId: {downloadItem.Id}");
            }

            UpdateDownloadButtonText();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error with download action", ex);
            DownloadButtonText = _localization.Format("ErrorLabel", ex.Message);
            await Task.Delay(3000);
            UpdateDownloadButtonText();
        }
    }

    private void SubscribeToDownloadEvents()
    {
        if (_isSubscribedToDownloadEvents) return;

        WeakReferenceMessenger.Default.Register<DownloadProgressChangedMessage>(this, (r, m) =>
            ((BookDetailViewModel)r).OnDownloadProgress(m.Value));
        WeakReferenceMessenger.Default.Register<DownloadCompletedMessage>(this, (r, m) =>
            ((BookDetailViewModel)r).OnDownloadCompleted(m.Value));
        WeakReferenceMessenger.Default.Register<DownloadFailedMessage>(this, (r, m) =>
            ((BookDetailViewModel)r).OnDownloadFailed(m.Value));
        _isSubscribedToDownloadEvents = true;
    }

    private void UnsubscribeFromDownloadEvents()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _isSubscribedToDownloadEvents = false;
    }

    private void OnDownloadProgress(DownloadProgressEventArgs e)
    {
        DownloadProgressPercent = Math.Min((int)e.Progress, 100);
        DownloadStatusText = _localization.Format("DownloadingProgress", DownloadProgressPercent);
        DownloadButtonText = DownloadStatusText;
    }

    private void OnDownloadCompleted(DownloadItem e)
    {
        _logger.Log($"Download completed for: {e.Title}");
        if (AudioBook != null)
            AudioBook.IsDownloaded = true;
        IsDownloaded = true;
        IsDownloading = false;
        UpdateDownloadButtonText();
        UnsubscribeFromDownloadEvents();
    }

    private void OnDownloadFailed(DownloadItem e)
    {
        _logger.LogWarning($"Download failed for: {e.Title} - {e.ErrorMessage}");
        DownloadButtonText = _localization.Format("FailedLabel", e.ErrorMessage);
        IsDownloading = false;
        UnsubscribeFromDownloadEvents();
    }

    private void UpdateDownloadButtonText()
    {
        if (!IsDownloading)
            DownloadButtonText = IsDownloaded ? _localization.GetString("RemoveDownload") : _localization.GetString("Download");
    }

    public void Dispose()
    {
        UnsubscribeFromDownloadEvents();
    }
}
