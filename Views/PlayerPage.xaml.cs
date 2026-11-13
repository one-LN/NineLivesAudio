using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Helpers;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using NineLivesAudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NineLivesAudio.Views;

public sealed partial class PlayerPage : Page
{
    public PlayerViewModel ViewModel { get; }
    private readonly ILoggingService _logger;
    private readonly IMetadataNormalizer _normalizer;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILocalizationService _localization;
    private bool _isUserSeeking = false;
    private bool _isChapterMode = false;

    // Cover art cache to prevent flashing on frequent updates
    private string? _lastCoverPath;
    private BitmapImage? _cachedCoverImage;

    public PlayerPage()
    {
        ViewModel = App.Services.GetRequiredService<PlayerViewModel>();
        _logger = App.Services.GetRequiredService<ILoggingService>();
        _normalizer = App.Services.GetRequiredService<IMetadataNormalizer>();
        _playbackService = App.Services.GetRequiredService<IAudioPlaybackService>();
        _apiService = App.Services.GetRequiredService<IAudioBookshelfApiService>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        this.InitializeComponent();
        RegisterChapterAccelerators();
    }

    /// <summary>
    /// Ctrl+Shift+Left / Ctrl+Shift+Right jump to the previous / next chapter.
    /// Registered in code so no XAML enum-flag parsing is involved.
    /// </summary>
    private void RegisterChapterAccelerators()
    {
        AddAccelerator(PrevChapterButton, Windows.System.VirtualKey.Left, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift);
        AddAccelerator(NextChapterButton, Windows.System.VirtualKey.Right, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift);
    }

    private static void AddAccelerator(Button? button, Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers)
    {
        if (button == null) return;
        button.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = key, Modifiers = modifiers });
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalization();
        _localization.LanguageChanged -= ApplyLocalization;
        _localization.LanguageChanged += ApplyLocalization;
        _logger.Log("PlayerPage loaded");
        ViewModel.Initialize();

        // Subscribe to ViewModel property changes
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Subscribe to playback events via Messenger for real-time updates
        WeakReferenceMessenger.Default.Register<PositionChangedMessage>(this, (r, m) =>
            ((PlayerPage)r).PlaybackService_PositionChanged(m.Value));
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
            ((PlayerPage)r).PlaybackService_StateChanged(m.Value));
        WeakReferenceMessenger.Default.Register<TrackChangedMessage>(this, (r, m) =>
            ((PlayerPage)r).PlaybackService_TrackChanged(m.Value));
        WeakReferenceMessenger.Default.Register<ChapterChangedMessage>(this, (r, m) =>
            ((PlayerPage)r).PlaybackService_ChapterChanged(m.Value));

        // Initial UI update
        UpdateAllUI();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _localization.LanguageChanged -= ApplyLocalization;
        _logger.Log("PlayerPage unloaded, cleaning up event subscriptions");

        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);

        if (ViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerViewModel.CurrentAudioBook):
                    UpdateBookUI();
                    break;
                case nameof(PlayerViewModel.IsPlaying):
                case nameof(PlayerViewModel.IsLoading):
                case nameof(PlayerViewModel.PlaybackSpeed):
                case nameof(PlayerViewModel.Volume):
                case nameof(PlayerViewModel.SleepTimerMinutes):
                case nameof(PlayerViewModel.SleepTimerText):
                    UpdateControlsUI();
                    break;
                // Position/time changes are handled by PositionChanged event — ignore here
            }
        });
    }

    private void PlaybackService_PositionChanged(TimeSpan position)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isUserSeeking) return;
            RefreshProgressDisplay(position);
        });
    }

    private void PlaybackService_StateChanged(PlaybackStateChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _logger.LogDebug($"PlayerPage received state change: {e.State}");
            UpdateAllUI();
        });
    }

    private void PlaybackService_TrackChanged(int trackIndex)
    {
        DispatcherQueue.TryEnqueue(() => UpdateTrackIndicator());
    }

    private void UpdateAllUI()
    {
        UpdateControlsUI();
        UpdateBookUI();
    }

    private void UpdateControlsUI()
    {
        var audioBook = ViewModel.CurrentAudioBook;
        var isPlaying = ViewModel.IsPlaying;
        var isLoading = ViewModel.IsLoading;

        // Visibility states
        bool hasBook = audioBook != null;
        EmptyStatePanel.Visibility = (!hasBook && !isLoading) ? Visibility.Visible : Visibility.Collapsed;
        PlayerContent.Visibility = hasBook ? Visibility.Visible : Visibility.Collapsed;
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

        // Play/Pause icon
        PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";

        // Speed text
        SpeedText.Text = $"{ViewModel.PlaybackSpeed:0.0}x";

        // Sleep timer
        SleepButtonText.Text = ViewModel.SleepTimerMinutes.HasValue ? $"{ViewModel.SleepTimerMinutes}m" : _localization.GetString("Off");
        SleepTimerPanel.Visibility = ViewModel.SleepTimerMinutes.HasValue ? Visibility.Visible : Visibility.Collapsed;
        SleepTimerStatusText.Text = ViewModel.SleepTimerText;

        // Volume
        VolumeSlider.Value = ViewModel.Volume;
        UpdateVolumeIcon();

        // Time texts — rendered through the mode-aware helper below. Writing the
        // book-global ViewModel strings here used to clobber the chapter display as
        // soon as the state changed (i.e. exactly when the next chapter started).
        if (!_isUserSeeking)
            RefreshProgressDisplay();
    }

    private void UpdateBookUI()
    {
        var audioBook = ViewModel.CurrentAudioBook;
        if (audioBook == null) return;

        // Use normalizer for clean display
        var normalized = _normalizer.Normalize(audioBook);
        TitleText.Text = normalized.DisplayTitle;
        AuthorText.Text = normalized.DisplayAuthor;

        // Series info
        if (!string.IsNullOrEmpty(normalized.DisplaySeries))
        {
            SeriesText.Text = normalized.DisplaySeries;
            SeriesText.Visibility = Visibility.Visible;
        }
        else
        {
            SeriesText.Visibility = Visibility.Collapsed;
        }

        // Source badge (local vs streaming)
        UpdateSourceBadge();

        // Track indicator (multi-file)
        UpdateTrackIndicator();

        // Chapter UI
        UpdateChapterUI();

        // Cover image — only reassign if the cover path actually changed
        var coverPath = audioBook.CoverPath;
        if (coverPath != _lastCoverPath)
        {
            _lastCoverPath = coverPath;
            _cachedCoverImage = CoverImageService.LoadCover(coverPath, 800, 800);
            CoverImage.Source = _cachedCoverImage;
        }
    }

    private void UpdateSourceBadge()
    {
        var isLocal = _playbackService.IsPlayingLocalFile;
        var state = _playbackService.State;

        // Only show badge when actively loaded/playing
        if (state == PlaybackState.Stopped)
        {
            SourceBadge.Visibility = Visibility.Collapsed;
            return;
        }

        SourceBadge.Visibility = Visibility.Visible;

        if (isLocal)
        {
            SourceBadge.Background = (Brush)Application.Current.Resources["NebulaDarkBrush"];
            SourceBadgeIcon.Glyph = "\uE73E"; // Checkmark
            SourceBadgeText.Text = _localization.GetString("PlayingLocal");
        }
        else
        {
            SourceBadge.Background = (Brush)Application.Current.Resources["VoidElevatedBrush"];
            SourceBadgeIcon.Glyph = "\uE753"; // Cloud/wifi
            SourceBadgeText.Text = _localization.GetString("StreamingFromServer");
        }
    }

    private void UpdateTrackIndicator()
    {
        var totalTracks = _playbackService.TotalTracks;
        if (totalTracks > 1)
        {
            TrackIndicator.Text = $"Track {_playbackService.CurrentTrackIndex + 1} of {totalTracks}";
            TrackIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            TrackIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void PlaybackService_ChapterChanged(Chapter? chapter)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateChapterUI();
            if (_isChapterMode)
            {
                RefreshProgressDisplay();
            }
        });
    }

    private void UpdateChapterUI()
    {
        var chapters = _playbackService.Chapters;
        var hasChapters = chapters.Count > 0;

        ChapterNameText.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        PrevChapterButton.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        NextChapterButton.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        ChaptersButtonPanel.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        ChaptersColumn.Width = hasChapters ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        // Show/hide chapter mode toggle
        ChapterModePanel.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;

        // If no chapters and chapter mode was on, turn it off
        if (!hasChapters && _isChapterMode)
        {
            _isChapterMode = false;
            ChapterModeToggle.IsOn = false;
        }

        var current = _playbackService.CurrentChapter;
        if (current != null)
        {
            ChapterNameText.Text = current.Title;
        }
        else if (hasChapters)
        {
            ChapterNameText.Text = _localization.Format("ChapterCount", chapters.Count);
        }
    }

    /// <summary>
    /// Seeks to the start of a chapter and refreshes the UI.
    /// Never throws: failures are logged, so async-void event handlers stay safe.
    /// </summary>
    private async Task SeekToChapterAsync(int chapterIndex)
    {
        var chapters = _playbackService.Chapters;
        if (chapterIndex < 0 || chapterIndex >= chapters.Count) return;

        try
        {
            await _playbackService.SeekToChapterAsync(chapterIndex);
            RefreshProgressDisplay();
        }
        catch (Exception ex)
        {
            _logger.LogError($"[Player] Chapter seek failed (index={chapterIndex})", ex);
        }
    }

    private async void PrevChapter_Click(object sender, RoutedEventArgs e)
    {
        var idx = _playbackService.CurrentChapterIndex;
        // At the first chapter, restart it; otherwise go back one chapter.
        await SeekToChapterAsync(Math.Max(idx - 1, 0));
    }

    private async void NextChapter_Click(object sender, RoutedEventArgs e)
    {
        var next = _playbackService.CurrentChapterIndex + 1;
        if (next < _playbackService.Chapters.Count)
            await SeekToChapterAsync(next);
    }

    private void ChaptersButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var chapters = _playbackService.Chapters;
        var currentIdx = _playbackService.CurrentChapterIndex;

        for (int i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var item = new MenuFlyoutItem
            {
                // The playing chapter gets a play glyph; clicking any entry jumps to its start.
                Text = $"{i + 1}. {chapter.Title}",
                Icon = i == currentIdx ? new FontIcon { Glyph = "\uE768" } : null
            };

            var capturedIndex = i;
            item.Click += async (s, args) => await SeekToChapterAsync(capturedIndex);
            flyout.Items.Add(item);
        }

        // Books can have 100+ chapters; cap the menu height so it scrolls instead of overflowing.
        flyout.MenuFlyoutPresenterStyle = new Style(typeof(MenuFlyoutPresenter))
        {
            Setters = { new Setter(FrameworkElement.MaxHeightProperty, 520.0) }
        };

        flyout.ShowAt(ChaptersButton);
    }

    private void UpdateVolumeIcon()
    {
        var vol = ViewModel.Volume;
        if (vol <= 0) VolumeIcon.Glyph = "\uE74F";
        else if (vol < 0.3) VolumeIcon.Glyph = "\uE993";
        else if (vol < 0.7) VolumeIcon.Glyph = "\uE994";
        else VolumeIcon.Glyph = "\uE995";
    }


    private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Only seek when user is interacting with the slider
        if (sender is Slider slider && slider.FocusState != FocusState.Unfocused)
        {
            _isUserSeeking = true;

            if (_isChapterMode)
            {
                var chapter = _playbackService.CurrentChapter;
                if (chapter != null && chapter.Duration.TotalSeconds > 0)
                {
                    // Map slider percentage to absolute position within current chapter
                    var chapterPosition = chapter.Start + (chapter.Duration.TotalSeconds * e.NewValue / 100);
                    chapterPosition = Math.Clamp(chapterPosition, chapter.Start, chapter.End);
                    _ = _playbackService.SeekAsync(TimeSpan.FromSeconds(chapterPosition));
                }
            }
            else
            {
                _ = ViewModel.SeekCommand.ExecuteAsync(e.NewValue);
            }

            _isUserSeeking = false;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider)
        {
            _ = ViewModel.SetVolumeCommand.ExecuteAsync((float)e.NewValue);
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PlayPauseCommand.ExecuteAsync(null);
    }

    private async void SkipBack_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SkipBackwardCommand.ExecuteAsync(null);
    }

    private async void SkipForward_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SkipForwardCommand.ExecuteAsync(null);
    }

    private void CancelSleepTimer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelSleepTimerCommand.Execute(null);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private void PopOut_Click(object sender, RoutedEventArgs e)
    {
        App.OpenMiniPlayer();
    }

    private void GoToLibrary_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to library page
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(LibraryPage));
        }
    }

    private void SpeedButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();

        var speeds = new[] { 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f };

        foreach (var speed in speeds)
        {
            var item = new MenuFlyoutItem
            {
                Text = $"{speed:0.0}x",
                Icon = speed == ViewModel.PlaybackSpeed
                    ? new FontIcon { Glyph = "\uE73E" }
                    : null
            };

            var capturedSpeed = speed;
            item.Click += (s, args) =>
            {
                _ = ViewModel.SetPlaybackSpeedCommand.ExecuteAsync(capturedSpeed);
                SpeedText.Text = $"{capturedSpeed:0.0}x";
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(SpeedButton);
    }

    private void SleepButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();

        var options = new (int? minutes, string label)[]
        {
            (null, _localization.GetString("Off")),
            (5, _localization.Format("MinutesCount", 5)),
            (10, _localization.Format("MinutesCount", 10)),
            (15, _localization.Format("MinutesCount", 15)),
            (30, _localization.Format("MinutesCount", 30)),
            (45, _localization.Format("MinutesCount", 45)),
            (60, _localization.Format("HoursCount", 1)),
            (90, _localization.Format("HoursCount", "1.5")),
            (120, _localization.Format("HoursCount", 2))
        };

        foreach (var (minutes, label) in options)
        {
            var item = new MenuFlyoutItem
            {
                Text = label,
                Icon = minutes == ViewModel.SleepTimerMinutes
                    ? new FontIcon { Glyph = "\uE73E" }
                    : null
            };

            var capturedMinutes = minutes;
            item.Click += (s, args) =>
            {
                ViewModel.SetSleepTimerCommand.Execute(capturedMinutes);
                UpdateAllUI();
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(SleepButton);
    }

    private void ChapterModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _isChapterMode = ChapterModeToggle.IsOn;
        RefreshProgressDisplay();
    }

    private void RefreshProgressDisplay() => RefreshProgressDisplay(null);

    /// <summary>
    /// Single place that paints the slider and both time labels.
    /// In chapter mode everything is chapter-relative; otherwise it is book-relative.
    /// When the chapter is unknown (chapter list missing, or before the first chapter)
    /// it falls back to book-relative instead of leaving stale text behind.
    /// </summary>
    private void RefreshProgressDisplay(TimeSpan? positionOverride)
    {
        var position = positionOverride ?? _playbackService.Position;
        var duration = ViewModel.Duration;
        var chapter = _isChapterMode ? _playbackService.CurrentChapter : null;

        if (chapter != null && chapter.Duration.TotalSeconds > 0)
        {
            var chapterElapsed = Math.Clamp(
                position.TotalSeconds - chapter.Start, 0, chapter.Duration.TotalSeconds);

            ProgressSlider.Value = chapterElapsed / chapter.Duration.TotalSeconds * 100;
            CurrentTimeText.Text = TimeFormatHelper.FormatTimeSpan(TimeSpan.FromSeconds(chapterElapsed));
            RemainingTimeText.Text =
                $"-{TimeFormatHelper.FormatTimeSpan(TimeSpan.FromSeconds(chapter.Duration.TotalSeconds - chapterElapsed))}";
            return;
        }

        if (duration.TotalSeconds > 0)
            ProgressSlider.Value = Math.Clamp(position.TotalSeconds / duration.TotalSeconds * 100, 0, 100);

        CurrentTimeText.Text = TimeFormatHelper.FormatTimeSpan(position);
        var remaining = duration - position;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        RemainingTimeText.Text = $"-{TimeFormatHelper.FormatTimeSpan(remaining)}";
    }

    private async void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var audioBook = ViewModel.CurrentAudioBook;
        if (audioBook == null) return;

        var flyout = new MenuFlyout();

        // "Add bookmark here" item
        var addItem = new MenuFlyoutItem
        {
            Text = _localization.GetString("AddBookmarkHere"),
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        addItem.Click += async (s, args) =>
        {
            await CreateBookmarkAtCurrentPositionAsync();
        };
        flyout.Items.Add(addItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // Load existing bookmarks
        try
        {
            var bookmarks = await _apiService.GetBookmarksAsync(audioBook.Id);

            if (bookmarks.Count == 0)
            {
                var emptyItem = new MenuFlyoutItem
                {
                    Text = _localization.GetString("NoBookmarks"),
                    IsEnabled = false
                };
                flyout.Items.Add(emptyItem);
            }
            else
            {
                foreach (var bookmark in bookmarks)
                {
                    var subItem = new MenuFlyoutSubItem
                    {
                        Text = $"{bookmark.Title}  ({bookmark.TimeFormatted})",
                        Icon = new FontIcon { Glyph = "\uE8A4" }
                    };

                    var capturedTime = bookmark.Time;
                    var capturedItemId = audioBook.Id;

                    var goToItem = new MenuFlyoutItem
                    {
                        Text = _localization.GetString("GoToPosition"),
                        Icon = new FontIcon { Glyph = "\uE768" }
                    };
                    goToItem.Click += async (s, args) =>
                    {
                        await _playbackService.SeekAsync(TimeSpan.FromSeconds(capturedTime));
                    };
                    subItem.Items.Add(goToItem);

                    var deleteItem = new MenuFlyoutItem
                    {
                        Text = _localization.GetString("Delete"),
                        Icon = new FontIcon { Glyph = "\uE74D" }
                    };
                    deleteItem.Click += async (s, args) =>
                    {
                        var success = await _apiService.DeleteBookmarkAsync(capturedItemId, capturedTime);
                        if (success)
                            _logger.Log($"[Bookmarks] Deleted bookmark at {capturedTime:F1}s");
                    };
                    subItem.Items.Add(deleteItem);

                    flyout.Items.Add(subItem);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"Failed to load bookmarks: {ex.Message}");
            var errorItem = new MenuFlyoutItem
            {
                Text = _localization.GetString("FailedLoadBookmarks"),
                IsEnabled = false
            };
            flyout.Items.Add(errorItem);
        }

        flyout.ShowAt(BookmarkButton);
    }

    private async Task CreateBookmarkAtCurrentPositionAsync()
    {
        var audioBook = ViewModel.CurrentAudioBook;
        if (audioBook == null) return;

        var currentTime = _playbackService.Position.TotalSeconds;

        // Generate a default title based on chapter info
        string defaultTitle;
        var chapter = _playbackService.CurrentChapter;
        var chapterIndex = _playbackService.CurrentChapterIndex;
        if (chapter != null && chapterIndex >= 0)
        {
            var chapterElapsed = TimeSpan.FromSeconds(currentTime - chapter.Start);
            defaultTitle = $"{chapter.Title} - {TimeFormatHelper.FormatTimeSpan(chapterElapsed)}";
        }
        else
        {
            defaultTitle = _localization.Format("BookmarkTitle", TimeFormatHelper.FormatTimeSpan(_playbackService.Position));
        }

        // Show a ContentDialog to let user edit the bookmark title
        var dialog = new ContentDialog
        {
            Title = _localization.GetString("AddBookmark"),
            PrimaryButtonText = _localization.GetString("Save"),
            CloseButtonText = _localization.GetString("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var titleInput = new TextBox
        {
            Text = defaultTitle,
            PlaceholderText = _localization.GetString("BookmarkName"),
            SelectionStart = 0,
            SelectionLength = defaultTitle.Length
        };
        dialog.Content = titleInput;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var title = string.IsNullOrWhiteSpace(titleInput.Text) ? defaultTitle : titleInput.Text;
            var success = await _apiService.CreateBookmarkAsync(audioBook.Id, title, currentTime);
            if (success)
            {
                _logger.Log($"[Bookmarks] Created: '{title}' at {currentTime:F1}s");
            }
            else
            {
                _logger.LogWarning($"[Bookmarks] Failed to create bookmark");
            }
        }
    }

    private void ApplyLocalization()
    {
        if (NoBookPlayingText != null) NoBookPlayingText.Text = _localization.GetString("NoBookPlaying");
        if (NoBookPlayingSubtitleText != null) NoBookPlayingSubtitleText.Text = _localization.GetString("NoBookPlayingSubtitle");
        if (BookLabelText != null) BookLabelText.Text = _localization.GetString("Book");
        if (ChapterLabelText != null) ChapterLabelText.Text = _localization.GetString("Chapter");
        if (SpeedLabelText != null) SpeedLabelText.Text = _localization.GetString("Speed");
        if (SleepLabelText != null) SleepLabelText.Text = _localization.GetString("Sleep");
        if (VolumeLabelText != null) VolumeLabelText.Text = _localization.GetString("Volume");
        if (ChaptersLabelText != null) ChaptersLabelText.Text = _localization.GetString("Chapters");
        if (BookmarksLabelText != null) BookmarksLabelText.Text = _localization.GetString("Bookmarks");
        if (LoadingAudiobookText != null) LoadingAudiobookText.Text = _localization.GetString("LoadingAudiobook");

        // Tooltips are not bound, so keep them in sync with the language too.
        ToolTipService.SetToolTip(PrevChapterButton, _localization.GetString("PrevChapterTip"));
        ToolTipService.SetToolTip(NextChapterButton, _localization.GetString("NextChapterTip"));
        ToolTipService.SetToolTip(SkipBackButton, _localization.GetString("SkipBackTip"));
        ToolTipService.SetToolTip(SkipForwardButton, _localization.GetString("SkipForwardTip"));
    }

}
