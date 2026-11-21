using NineLivesAudio.Models;
using NineLivesAudio.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace NineLivesAudio.Controls;

public sealed partial class BookCard : UserControl
{
    public event EventHandler<AudioBook>? PlayRequested;
    public event EventHandler<AudioBook>? DownloadRequested;

    private AudioBook? _audioBook;

    public AudioBook? AudioBook
    {
        get => _audioBook;
        set
        {
            _audioBook = value;
            UpdateUI();
        }
    }

    public BookCard()
    {
        this.InitializeComponent();
    }

    private void UpdateUI()
    {
        if (_audioBook == null)
        {
            TitleText.Text = "";
            AuthorText.Text = "";
            CoverImage.Source = null;
            ProgressBar.Visibility = Visibility.Collapsed;
            DownloadedBadge.Visibility = Visibility.Collapsed;
            return;
        }

        TitleText.Text = _audioBook.Title;
        AuthorText.Text = _audioBook.Author;

        CoverImage.Source = CoverImageService.LoadCover(_audioBook.CoverPath, 300, 300);

        if (_audioBook.Progress > 0)
        {
            ProgressBar.Value = _audioBook.Progress;
            ProgressBar.Visibility = Visibility.Visible;
        }
        else
        {
            ProgressBar.Visibility = Visibility.Collapsed;
        }

        DownloadedBadge.Visibility = _audioBook.IsDownloaded
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Grid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var animation = new DoubleAnimation
        {
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(150))
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, HoverOverlay);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Begin();
    }

    private void Grid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        var animation = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150))
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, HoverOverlay);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Begin();
    }

    private void Grid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed && _audioBook != null)
        {
            ShowContextMenu(point.Position);
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audioBook != null)
        {
            PlayRequested?.Invoke(this, _audioBook);
        }
    }

    private void ShowContextMenu(Windows.Foundation.Point position)
    {
        if (_audioBook == null) return;

        var menu = new MenuFlyout();

        var playItem = new MenuFlyoutItem
        {
            Text = "Play",
            Icon = new FontIcon { Glyph = "\uE768" }
        };
        playItem.Click += (s, e) => PlayRequested?.Invoke(this, _audioBook);
        menu.Items.Add(playItem);

        if (!_audioBook.IsDownloaded)
        {
            var downloadItem = new MenuFlyoutItem
            {
                Text = "Download",
                Icon = new FontIcon { Glyph = "\uE896" }
            };
            downloadItem.Click += (s, e) => DownloadRequested?.Invoke(this, _audioBook);
            menu.Items.Add(downloadItem);
        }

        menu.ShowAt(this, position);
    }
}
