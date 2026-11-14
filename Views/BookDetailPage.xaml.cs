using NineLivesAudio.Helpers;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using NineLivesAudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace NineLivesAudio.Views;

public sealed partial class BookDetailPage : Page
{
    private AudioBook? _audioBook;
    private bool _isUpdatingToggle;
    private readonly BookDetailViewModel _viewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;

    public BookDetailPage()
    {
        this.InitializeComponent();
        _viewModel = App.Services.GetRequiredService<BookDetailViewModel>();
        _libraryViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        _logger = App.Services.GetRequiredService<ILoggingService>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        this.Unloaded += Page_Unloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        _localization.LanguageChanged -= ApplyLocalization;
        _localization.LanguageChanged += ApplyLocalization;

        if (e.Parameter is AudioBook audioBook)
        {
            _audioBook = audioBook;
            _viewModel.Initialize(audioBook);
            LoadBookDetails();
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _localization.LanguageChanged -= ApplyLocalization;
    }

    private void LoadBookDetails()
    {
        if (_audioBook == null) return;

        _logger.Log($"Loading book details: {_audioBook.Title}");

        // Title — populate both wide and narrow
        TitleText.Text = _audioBook.Title;
        TitleTextNarrow.Text = _audioBook.Title;

        // Author (clickable)
        AuthorText.Text = $"by {_audioBook.Author}";
        AuthorTextNarrow.Text = $"by {_audioBook.Author}";

        // Narrator
        if (!string.IsNullOrEmpty(_audioBook.Narrator))
        {
            var narratorDisplay = _localization.Format("NarratedBy", _audioBook.Narrator);
            NarratorText.Text = narratorDisplay;
            NarratorText.Visibility = Visibility.Visible;
            NarratorTextNarrow.Text = narratorDisplay;
            NarratorTextNarrow.Visibility = Visibility.Visible;
        }

        // Series (clickable link)
        if (!string.IsNullOrEmpty(_audioBook.SeriesName))
        {
            var seriesDisplay = _audioBook.SeriesName;
            if (!string.IsNullOrEmpty(_audioBook.SeriesSequence))
                seriesDisplay += $" #{_audioBook.SeriesSequence}";
            SeriesText.Text = seriesDisplay;
            SeriesLink.Visibility = Visibility.Visible;
            SeriesTextNarrow.Text = seriesDisplay;
            SeriesLinkNarrow.Visibility = Visibility.Visible;
        }

        // Duration
        var durationDisplay = _viewModel.FormattedDuration;
        DurationText.Text = durationDisplay;
        DurationTextNarrow.Text = durationDisplay;

        // Added date
        if (_audioBook.AddedAt.HasValue)
        {
            var addedDisplay = _localization.Format("AddedOn", AppLanguage.FormatShortDate(_audioBook.AddedAt.Value));
            AddedAtText.Text = addedDisplay;
            AddedAtText.Visibility = Visibility.Visible;
            AddedAtTextNarrow.Text = addedDisplay;
            AddedAtTextNarrow.Visibility = Visibility.Visible;
        }

        // Cover image — set both wide and narrow images
        var coverBitmap = CoverImageService.LoadCover(_audioBook.CoverPath, 800, 800);
        CoverImage.Source = coverBitmap;
        CoverImageNarrow.Source = coverBitmap;

        // Show chapter toggle if chapters exist
        var hasChapters = _audioBook.Chapters.Count > 0;
        ChapterToggle.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;
        ChapterToggleNarrow.Visibility = hasChapters ? Visibility.Visible : Visibility.Collapsed;

        // Progress display
        UpdateProgressDisplay();

        // Metadata details panel
        BuildMetadataPanel();

        // Genres
        if (_audioBook.Genres.Count > 0)
        {
            GenresRepeater.ItemsSource = _audioBook.Genres;
            GenresPanel.Visibility = Visibility.Visible;
        }

        // Tags
        if (_audioBook.Tags.Count > 0)
        {
            TagsRepeater.ItemsSource = _audioBook.Tags;
            TagsPanel.Visibility = Visibility.Visible;
        }

        // Description
        if (!string.IsNullOrEmpty(_audioBook.Description))
        {
            DescriptionText.Text = _audioBook.Description;
            DescriptionPanel.Visibility = Visibility.Visible;
        }

        // Download state
        UpdateDownloadState();

        // Chapters
        LoadChapters();

        // Tracks
        LoadTracks();
    }

    private void ChapterToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToggle) return;
        _isUpdatingToggle = true;
        try
        {
            // Sync both toggles
            var isOn = (sender as ToggleSwitch)?.IsOn ?? false;
            _viewModel.ShowChapterProgress = isOn;
            if (ChapterToggle.IsOn != isOn) ChapterToggle.IsOn = isOn;
            if (ChapterToggleNarrow.IsOn != isOn) ChapterToggleNarrow.IsOn = isOn;
            UpdateProgressDisplay();
        }
        finally
        {
            _isUpdatingToggle = false;
        }
    }

    private void UpdateProgressDisplay()
    {
        if (_audioBook == null) return;

        var (progressText, progressPercent, chapterInfo) = _viewModel.GetProgressInfo();

        ProgressText.Text = progressText;
        ProgressTextNarrow.Text = progressText;
        ProgressBar.Value = progressPercent;
        ProgressBarNarrow.Value = progressPercent;

        if (chapterInfo != null)
        {
            ChapterInfoText.Text = chapterInfo;
            ChapterInfoTextNarrow.Text = chapterInfo;
            ChapterInfoText.Visibility = Visibility.Visible;
            ChapterInfoTextNarrow.Visibility = Visibility.Visible;
        }
        else
        {
            ChapterInfoText.Visibility = Visibility.Collapsed;
            ChapterInfoTextNarrow.Visibility = Visibility.Collapsed;
        }

        var playLabel = _audioBook.ProgressPercent > 0 ? _localization.GetString("Continue") : _localization.GetString("Play");
        PlayButtonText.Text = playLabel;
        PlayButtonTextNarrow.Text = playLabel;
    }

    private void BuildMetadataPanel()
    {
        if (_audioBook == null) return;

        MetadataItemsPanel.Children.Clear();

        AddMetadataRow("Title", _audioBook.Title);
        AddMetadataRow("Author", _audioBook.Author);

        if (!string.IsNullOrEmpty(_audioBook.Narrator))
            AddMetadataRow("Narrator", _audioBook.Narrator);

        if (!string.IsNullOrEmpty(_audioBook.SeriesName))
        {
            var seriesDisplay = _audioBook.SeriesName;
            if (!string.IsNullOrEmpty(_audioBook.SeriesSequence))
                seriesDisplay += $" (Book {_audioBook.SeriesSequence})";
            AddMetadataRow("Series", seriesDisplay);
        }

        AddMetadataRow("Duration", TimeFormatHelper.FormatDuration(_audioBook.Duration));

        if (_audioBook.AudioFiles.Count > 0)
        {
            var totalSize = _audioBook.AudioFiles.Sum(f => f.Size);
            if (totalSize > 0)
                AddMetadataRow("Size", TimeFormatHelper.FormatSize(totalSize));
            AddMetadataRow("Files", $"{_audioBook.AudioFiles.Count} audio file{(_audioBook.AudioFiles.Count > 1 ? "s" : "")}");
        }

        if (_audioBook.AddedAt.HasValue)
            AddMetadataRow(_localization.GetString("Added"), AppLanguage.FormatShortDate(_audioBook.AddedAt.Value));

        if (_audioBook.IsFinished)
            AddMetadataRow("Status", "Finished");
        else if (_audioBook.ProgressPercent > 0)
            AddMetadataRow("Status", $"{(int)_audioBook.ProgressPercent}% complete");
    }

    private void AddMetadataRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["MistFaintBrush"],
            FontSize = 13
        };
        Grid.SetColumn(labelBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["StarlightDimBrush"]
        };
        Grid.SetColumn(valueBlock, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        MetadataItemsPanel.Children.Add(grid);
    }

    private void LoadChapters()
    {
        var chapterItems = _viewModel.GetChapterDisplayItems();
        if (chapterItems.Count == 0)
        {
            ChaptersPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ChaptersHeader.Text = _localization.Format("ChaptersCount", chapterItems.Count);
        ChaptersPanel.Visibility = Visibility.Visible;

        ChaptersRepeater.ItemTemplate = CreateChapterTemplate();
        ChaptersRepeater.ItemsSource = chapterItems;
    }

    private DataTemplate CreateChapterTemplate()
    {
        var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <Grid CornerRadius=""4"" Padding=""12,8""
                      Background=""{Binding ChapterBackground}"">
                    <Grid.RowDefinitions>
                        <RowDefinition Height=""Auto""/>
                        <RowDefinition Height=""Auto""/>
                    </Grid.RowDefinitions>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""40""/>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column=""0"" Text=""{Binding Index}"" Foreground=""#FF6B7280""/>
                    <TextBlock Grid.Column=""1"" Text=""{Binding Title}"" TextTrimming=""CharacterEllipsis""
                               FontWeight=""{Binding TitleWeight}"" Foreground=""#FFE0E0E8""/>
                    <TextBlock Grid.Column=""2"" Text=""{Binding StatusIcon}"" Margin=""8,0,0,0"" FontSize=""12""
                               Foreground=""#FFC5A55A""/>
                    <TextBlock Grid.Column=""3"" Text=""{Binding StartTime}"" Foreground=""#FF6B7280"" Margin=""8,0,0,0""/>
                    <ProgressBar Grid.Row=""1"" Grid.ColumnSpan=""4""
                                 Minimum=""0"" Maximum=""100""
                                 Value=""{Binding ProgressPercent}""
                                 Height=""3"" Margin=""0,4,0,0""
                                 CornerRadius=""2""
                                 Foreground=""#FFC5A55A""
                                 Visibility=""{Binding ProgressBarVisibility}""/>
                </Grid>
            </DataTemplate>");
        return template;
    }

    private void LoadTracks()
    {
        var trackItems = _viewModel.GetTrackDisplayItems();
        if (trackItems.Count == 0)
        {
            TracksPanel.Visibility = Visibility.Collapsed;
            return;
        }

        TracksHeader.Text = _localization.Format("TracksCount", trackItems.Count);

        TracksRepeater.ItemTemplate = CreateTrackTemplate();
        TracksRepeater.ItemsSource = trackItems;
    }

    private DataTemplate CreateTrackTemplate()
    {
        var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <Grid Background=""#FF111827""
                      CornerRadius=""4""
                      Padding=""12,8"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""40""/>
                        <ColumnDefinition Width=""*""/>
                        <ColumnDefinition Width=""Auto""/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Grid.Column=""0"" Text=""{Binding Index}"" Foreground=""#FF6B7280""/>
                    <TextBlock Grid.Column=""1"" Text=""{Binding Title}"" TextTrimming=""CharacterEllipsis""
                               Foreground=""#FFE0E0E8""/>
                    <TextBlock Grid.Column=""2"" Text=""{Binding Duration}"" Foreground=""#FF6B7280"" Margin=""8,0,0,0""/>
                </Grid>
            </DataTemplate>");
        return template;
    }

    private void UpdateDownloadState()
    {
        if (_audioBook == null) return;

        var isDownloaded = _viewModel.IsDownloaded;

        DownloadedBadge.Visibility = isDownloaded ? Visibility.Visible : Visibility.Collapsed;
        DownloadedBadgeNarrow.Visibility = isDownloaded ? Visibility.Visible : Visibility.Collapsed;
        DownloadIcon.Glyph = isDownloaded ? "\uE74D" : "\uE896";
        DownloadIconNarrow.Glyph = isDownloaded ? "\uE74D" : "\uE896";
        DownloadButtonText.Text = _viewModel.DownloadButtonText;
        DownloadButtonTextNarrow.Text = _viewModel.DownloadButtonText;
    }


    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void AuthorLink_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook == null) return;

        // Navigate back to library and set author filter
        _libraryViewModel.CurrentViewMode = ViewMode.Author;
        _libraryViewModel.SelectedGroupFilter = _audioBook.Author;

        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private void SeriesLink_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook?.SeriesName == null) return;

        // Navigate back to library and set series filter
        _libraryViewModel.CurrentViewMode = ViewMode.Series;
        _libraryViewModel.SelectedGroupFilter = _audioBook.SeriesName;

        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook == null) return;

        _logger.Log($"Play button clicked for: {_audioBook.Title}");

        try
        {
            PlayButton.IsEnabled = false;
            PlayButtonNarrow.IsEnabled = false;
            DownloadButton.IsEnabled = false;
            DownloadButtonNarrow.IsEnabled = false;
            PlayIcon.Glyph = "\uE916"; // Loading icon
            PlayIconNarrow.Glyph = "\uE916";
            PlayButtonText.Text = "Loading...";
            PlayButtonTextNarrow.Text = "Loading...";

            await _viewModel.PlayCommand.ExecuteAsync(null);

            // Navigate to player if play succeeded (ViewModel loaded + started playback)
            if (!_viewModel.IsPlayLoading)
            {
                Frame.Navigate(typeof(PlayerPage));
            }
        }
        finally
        {
            PlayButton.IsEnabled = true;
            PlayButtonNarrow.IsEnabled = true;
            DownloadButton.IsEnabled = true;
            DownloadButtonNarrow.IsEnabled = true;
            PlayIcon.Glyph = "\uE768";
            PlayIconNarrow.Glyph = "\uE768";
            PlayButtonText.Text = _viewModel.PlayButtonText;
            PlayButtonTextNarrow.Text = _viewModel.PlayButtonText;
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook == null) return;

        try
        {
            DownloadButton.IsEnabled = false;
            DownloadButtonNarrow.IsEnabled = false;

            // Subscribe to ViewModel property changes for UI updates during download
            _viewModel.PropertyChanged += ViewModel_DownloadPropertyChanged;

            await _viewModel.ToggleDownloadCommand.ExecuteAsync(null);
            UpdateDownloadState();
        }
        catch (Exception ex)
        {
            _logger.LogError("Error with download action", ex);
            DownloadButtonText.Text = _localization.Format("ErrorLabel", ex.Message);
            DownloadButtonTextNarrow.Text = _localization.Format("ErrorLabel", ex.Message);
            await Task.Delay(3000);
            UpdateDownloadState();
        }
        finally
        {
            DownloadButton.IsEnabled = true;
            DownloadButtonNarrow.IsEnabled = true;
        }
    }

    private void ViewModel_DownloadPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(BookDetailViewModel.DownloadButtonText):
                    DownloadButtonText.Text = _viewModel.DownloadButtonText;
                    DownloadButtonTextNarrow.Text = _viewModel.DownloadButtonText;
                    break;
                case nameof(BookDetailViewModel.IsDownloaded):
                    UpdateDownloadState();
                    break;
                case nameof(BookDetailViewModel.IsDownloading):
                    if (!_viewModel.IsDownloading)
                        _viewModel.PropertyChanged -= ViewModel_DownloadPropertyChanged;
                    break;
            }
        });
    }

    private void ApplyLocalization()
    {
        if (DetailsTitleText != null) DetailsTitleText.Text = _localization.GetString("Details");
        if (GenresTitleText != null) GenresTitleText.Text = _localization.GetString("Genres");
        if (TagsTitleText != null) TagsTitleText.Text = _localization.GetString("Tags");
        if (DescriptionTitleText != null) DescriptionTitleText.Text = _localization.GetString("Description");
    }

}
