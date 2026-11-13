using NineLivesAudio.Helpers;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using NineLivesAudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NineLivesAudio.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }
    private readonly ILoggingService _logger;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILocalizationService _localization;
    private List<LibraryGroupItem> _currentGroups = new();
    private string? _selectedGroup;

    private readonly TapHelper _tapHelper = new();

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        _logger = App.Services.GetRequiredService<ILoggingService>();
        _apiService = App.Services.GetRequiredService<IAudioBookshelfApiService>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        this.InitializeComponent();
        UpdateViewModeButtons();
        UpdateStatusPill();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalization();
        _localization.LanguageChanged -= ApplyLocalization;
        _localization.LanguageChanged += ApplyLocalization;
        _logger.Log("LibraryPage loaded, initializing ViewModel");
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        await ViewModel.InitializeAsync();
        _logger.Log("LibraryPage ViewModel initialized");
        SyncTogglesToViewModel();
        UpdateViewDisplay();
        UpdateStatusPill();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _localization.LanguageChanged -= ApplyLocalization;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

        if (ViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ShowDownloadedOnly))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (DownloadedOnlyToggle.IsOn != ViewModel.ShowDownloadedOnly)
                    DownloadedOnlyToggle.IsOn = ViewModel.ShowDownloadedOnly;
            });
        }
    }

    private void SyncTogglesToViewModel()
    {
        DownloadedOnlyToggle.IsOn = ViewModel.ShowDownloadedOnly;
        HideFinishedToggle.IsOn = ViewModel.HideFinished;
    }

    private void UpdateStatusPill()
    {
        var isConnected = _apiService.IsAuthenticated;
        StatusText.Text = isConnected ? _localization.GetString("Connected") : _localization.GetString("Offline");

        // Update pill color
        if (isConnected)
        {
            StatusPill.Background = (Brush)Application.Current.Resources["NebulaDarkBrush"];
            StatusEllipse.Fill = (Brush)Application.Current.Resources["RitualSuccessBrush"];
        }
        else
        {
            StatusPill.Background = (Brush)Application.Current.Resources["VoidElevatedBrush"];
            StatusEllipse.Fill = (Brush)Application.Current.Resources["MistFaintBrush"];
        }
    }

    private void BookCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            // Subtle scale effect on hover
            card.Scale = new System.Numerics.Vector3(1.02f, 1.02f, 1f);
        }
    }

    private void BookCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card)
        {
            card.Scale = new System.Numerics.Vector3(1f, 1f, 1f);
        }
    }

    private void CoverImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _logger.LogDebug($"Cover image failed to load: {e.ErrorMessage}");
    }

    private void BookCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is AudioBook audioBook)
        {
            var point = e.GetCurrentPoint(element);
            if (point.Properties.IsRightButtonPressed)
                ShowBookContextMenu(element, audioBook, point.Position);
            else
                _tapHelper.OnPointerPressed(e, element, audioBook);
        }
    }

    private void BookCard_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            _tapHelper.OnPointerMoved(e, element);
    }

    private void BookCard_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var audioBook = _tapHelper.OnPointerReleased<AudioBook>();
        if (audioBook != null)
        {
            _logger.Log($"Opening book details: {audioBook.Title}");
            NavigateToBookDetail(audioBook);
        }
    }

    private void NavigateToBookDetail(AudioBook audioBook)
    {
        Frame.Navigate(typeof(BookDetailPage), audioBook);
    }

    private void ShowBookContextMenu(FrameworkElement element, AudioBook audioBook, Windows.Foundation.Point position)
    {
        var menu = new MenuFlyout();

        var viewItem = new MenuFlyoutItem
        {
            Text = _localization.GetString("ViewDetails"),
            Icon = new FontIcon { Glyph = "\uE8A5" }
        };
        viewItem.Click += (s, e) => NavigateToBookDetail(audioBook);
        menu.Items.Add(viewItem);

        var playItem = new MenuFlyoutItem
        {
            Text = "Play",
            Icon = new FontIcon { Glyph = "\uE768" }
        };
        playItem.Click += (s, e) => _ = ViewModel.PlayAudioBookCommand.ExecuteAsync(audioBook);
        menu.Items.Add(playItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        if (!audioBook.IsDownloaded)
        {
            var downloadItem = new MenuFlyoutItem
            {
                Text = "Download",
                Icon = new FontIcon { Glyph = "\uE896" }
            };
            downloadItem.Click += (s, e) => _ = ViewModel.DownloadAudioBookCommand.ExecuteAsync(audioBook);
            menu.Items.Add(downloadItem);
        }
        else
        {
            var removeItem = new MenuFlyoutItem
            {
                Text = _localization.GetString("RemoveDownload"),
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            removeItem.Click += (s, e) => _ = ViewModel.RemoveDownloadCommand.ExecuteAsync(audioBook);
            menu.Items.Add(removeItem);
        }

        menu.ShowAt(element, position);
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.CurrentSortMode = tag switch
            {
                "Title" => SortMode.Title,
                "Author" => SortMode.Author,
                "Progress" => SortMode.Progress,
                "RecentProgress" => SortMode.RecentProgress,
                _ => SortMode.Default
            };
        }
    }

    private void HideFinishedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.HideFinished = HideFinishedToggle.IsOn;
    }

    private void DownloadedOnlyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowDownloadedOnly = DownloadedOnlyToggle.IsOn;
    }

    private void ViewModeAll_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentViewMode = ViewMode.All;
        _selectedGroup = null;
        UpdateViewModeButtons();
        UpdateViewDisplay();
    }

    private void ViewModeSeries_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentViewMode = ViewMode.Series;
        _selectedGroup = null;
        UpdateViewModeButtons();
        UpdateViewDisplay();
    }

    private void ViewModeAuthor_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentViewMode = ViewMode.Author;
        _selectedGroup = null;
        UpdateViewModeButtons();
        UpdateViewDisplay();
    }

    private void ViewModeGenre_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentViewMode = ViewMode.Genre;
        _selectedGroup = null;
        UpdateViewModeButtons();
        UpdateViewDisplay();
    }

    private void BackToGroups_Click(object sender, RoutedEventArgs e)
    {
        _selectedGroup = null;
        UpdateViewDisplay();
    }

    private void UpdateViewModeButtons()
    {
        var activeBg = (Brush)Application.Current.Resources["SigilGoldBrush"];
        var activeFg = (Brush)Application.Current.Resources["VoidDeepBrush"];
        var inactiveBg = (Brush)Application.Current.Resources["VoidElevatedBrush"];
        var inactiveFg = (Brush)Application.Current.Resources["StarlightDimBrush"];

        SetViewModeButton(ViewModeAllButton, ViewModel.CurrentViewMode == ViewMode.All, activeBg, activeFg, inactiveBg, inactiveFg);
        SetViewModeButton(ViewModeSeriesButton, ViewModel.CurrentViewMode == ViewMode.Series, activeBg, activeFg, inactiveBg, inactiveFg);
        SetViewModeButton(ViewModeAuthorButton, ViewModel.CurrentViewMode == ViewMode.Author, activeBg, activeFg, inactiveBg, inactiveFg);
        SetViewModeButton(ViewModeGenreButton, ViewModel.CurrentViewMode == ViewMode.Genre, activeBg, activeFg, inactiveBg, inactiveFg);
    }

    private static void SetViewModeButton(Button button, bool isActive, Brush activeBg, Brush activeFg, Brush inactiveBg, Brush inactiveFg)
    {
        button.Background = isActive ? activeBg : inactiveBg;
        button.Foreground = isActive ? activeFg : inactiveFg;
    }

    private void UpdateViewDisplay()
    {
        if (ViewModel.CurrentViewMode == ViewMode.All)
        {
            // Show all books grid
            AllBooksView.Visibility = Visibility.Visible;
            GroupsView.Visibility = Visibility.Collapsed;
            GroupDetailView.Visibility = Visibility.Collapsed;
        }
        else if (_selectedGroup != null)
        {
            // Show books in selected group
            AllBooksView.Visibility = Visibility.Collapsed;
            GroupsView.Visibility = Visibility.Collapsed;
            GroupDetailView.Visibility = Visibility.Visible;
            LoadGroupDetail();
        }
        else
        {
            // Show group cards
            AllBooksView.Visibility = Visibility.Collapsed;
            GroupsView.Visibility = Visibility.Visible;
            GroupDetailView.Visibility = Visibility.Collapsed;
            LoadGroups();
        }
    }

    private void LoadGroups()
    {
        _currentGroups = ViewModel.CreateGroups().ToList();

        _logger.Log($"Loaded {_currentGroups.Count} groups for {ViewModel.CurrentViewMode}");

        GroupsRepeater.ItemTemplate = CreateGroupCardTemplate();
        GroupsRepeater.ItemsSource = _currentGroups;
    }

    private DataTemplate CreateGroupCardTemplate()
    {
        var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <Grid Background=""#FF111827""
                      CornerRadius=""8""
                      Padding=""16""
                      BorderBrush=""#FF3D4451""
                      BorderThickness=""1"">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width=""60""/>
                        <ColumnDefinition Width=""*""/>
                    </Grid.ColumnDefinitions>

                    <Border Grid.Column=""0""
                            Width=""60""
                            Height=""60""
                            CornerRadius=""4""
                            Background=""#FF1A2236"">
                        <Grid>
                            <FontIcon Glyph=""{Binding Icon}"" FontSize=""24"" Opacity=""0.5""
                                      HorizontalAlignment=""Center"" VerticalAlignment=""Center""
                                      Foreground=""#FF6B7280""/>
                            <Image Source=""{Binding CoverUrl}"" Stretch=""UniformToFill""/>
                        </Grid>
                    </Border>

                    <StackPanel Grid.Column=""1"" Margin=""12,0,0,0"" VerticalAlignment=""Center"">
                        <TextBlock Text=""{Binding Name}""
                                   FontWeight=""SemiBold""
                                   FontSize=""14""
                                   Foreground=""#FFE0E0E8""
                                   TextTrimming=""CharacterEllipsis""
                                   MaxLines=""2""
                                   TextWrapping=""Wrap""/>
                        <TextBlock Text=""{Binding BookCountText}""
                                   FontSize=""12""
                                   Foreground=""#FF6B7280""
                                   Margin=""0,4,0,0""/>
                    </StackPanel>
                </Grid>
            </DataTemplate>");
        return template;
    }

    private void GroupsRepeater_Loaded(object sender, RoutedEventArgs e)
    {
        // Hook up click events for group cards
        if (sender is ItemsRepeater repeater)
        {
            repeater.ElementPrepared += GroupsRepeater_ElementPrepared;
        }
    }

    private void GroupsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            element.PointerPressed -= GroupCard_PointerPressed;
            element.PointerPressed += GroupCard_PointerPressed;
            element.PointerMoved -= GroupCard_PointerMoved;
            element.PointerMoved += GroupCard_PointerMoved;
            element.PointerReleased -= GroupCard_PointerReleased;
            element.PointerReleased += GroupCard_PointerReleased;
        }
    }

    private void GroupCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is LibraryGroupItem group)
            _tapHelper.OnPointerPressed(e, element, group);
    }

    private void GroupCard_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            _tapHelper.OnPointerMoved(e, element);
    }

    private void GroupCard_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var group = _tapHelper.OnPointerReleased<LibraryGroupItem>();
        if (group != null)
        {
            _logger.Log($"Selected group: {group.Name}");
            _selectedGroup = group.Name;
            UpdateViewDisplay();
        }
    }

    private void LoadGroupDetail()
    {
        if (_selectedGroup == null) return;

        GroupDetailTitle.Text = _selectedGroup;

        var books = ViewModel.GetBooksForGroup(_selectedGroup);

        _logger.Log($"Group '{_selectedGroup}' has {books.Count} books");

        GroupBooksRepeater.ItemTemplate = CreateBookCardTemplate();
        GroupBooksRepeater.ItemsSource = books;

        // Hook up click events
        GroupBooksRepeater.ElementPrepared -= GroupBooksRepeater_ElementPrepared;
        GroupBooksRepeater.ElementPrepared += GroupBooksRepeater_ElementPrepared;
    }

    private void GroupBooksRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            element.PointerPressed -= DetailBookCard_PointerPressed;
            element.PointerPressed += DetailBookCard_PointerPressed;
            element.PointerMoved -= DetailBookCard_PointerMoved;
            element.PointerMoved += DetailBookCard_PointerMoved;
            element.PointerReleased -= DetailBookCard_PointerReleased;
            element.PointerReleased += DetailBookCard_PointerReleased;
        }
    }

    private void DetailBookCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is AudioBook audioBook)
        {
            var point = e.GetCurrentPoint(element);
            if (point.Properties.IsRightButtonPressed)
                ShowBookContextMenu(element, audioBook, point.Position);
            else
                _tapHelper.OnPointerPressed(e, element, audioBook);
        }
    }

    private void DetailBookCard_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            _tapHelper.OnPointerMoved(e, element);
    }

    private void DetailBookCard_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var audioBook = _tapHelper.OnPointerReleased<AudioBook>();
        if (audioBook != null)
        {
            _logger.Log($"Opening book from group: {audioBook.Title}");
            NavigateToBookDetail(audioBook);
        }
    }

    private DataTemplate CreateBookCardTemplate()
    {
        var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
            <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                <Grid Background=""#FF111827""
                      CornerRadius=""8""
                      Padding=""8""
                      BorderBrush=""#FF3D4451""
                      BorderThickness=""1"">
                    <Grid.RowDefinitions>
                        <RowDefinition Height=""*""/>
                        <RowDefinition Height=""Auto""/>
                        <RowDefinition Height=""Auto""/>
                        <RowDefinition Height=""Auto""/>
                    </Grid.RowDefinitions>

                    <Border Grid.Row=""0""
                            CornerRadius=""4""
                            Background=""#FF1A2236"">
                        <Grid>
                            <FontIcon Glyph=""&#xE8F1;""
                                      FontSize=""48""
                                      Opacity=""0.3""
                                      Foreground=""#FF6B7280""
                                      HorizontalAlignment=""Center""
                                      VerticalAlignment=""Center""/>
                            <Image Source=""{Binding CoverPath}"" Stretch=""UniformToFill""/>
                        </Grid>
                    </Border>

                    <TextBlock Grid.Row=""1""
                               Text=""{Binding Title}""
                               FontWeight=""SemiBold""
                               Foreground=""#FFE0E0E8""
                               TextTrimming=""CharacterEllipsis""
                               MaxLines=""2""
                               TextWrapping=""Wrap""
                               Margin=""0,8,0,4""/>

                    <TextBlock Grid.Row=""2""
                               Text=""{Binding Author}""
                               Foreground=""#FF6B7280""
                               FontSize=""12""
                               TextTrimming=""CharacterEllipsis""/>

                    <ProgressBar Grid.Row=""3""
                                 Value=""{Binding Progress}""
                                 Maximum=""1""
                                 Foreground=""#FFC5A55A""
                                 Margin=""0,8,0,0""/>
                </Grid>
            </DataTemplate>");
        return template;
    }

    private void ApplyLocalization()
    {
        if (LibraryTitleText != null) LibraryTitleText.Text = _localization.GetString("LibraryTitle");
        if (LoadingLibraryText != null) LoadingLibraryText.Text = _localization.GetString("LoadingLibrary");
        if (LocalBadgeText != null) LocalBadgeText.Text = _localization.GetString("Local");
        ViewModel.RefreshLocalizedStrings();
        UpdateStatusPill();
    }

}
