using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
// BitmapImage via CoverImageService

namespace NineLivesAudio.Controls;

public sealed partial class MiniPlayer : UserControl
{
    private readonly IAudioPlaybackService _playbackService;
    private readonly ILocalizationService _localization;
    private bool _isPlaying;

    public MiniPlayer()
    {
        this.InitializeComponent();

        _playbackService = App.Services.GetRequiredService<IAudioPlaybackService>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        RegisterMessenger();
        UpdateUI();
    }

    private void RegisterMessenger()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
            ((MiniPlayer)r).OnPlaybackStateChanged(m.Value));
        WeakReferenceMessenger.Default.Register<PositionChangedMessage>(this, (r, m) =>
            ((MiniPlayer)r).OnPositionChanged(m.Value));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-subscribe in case the control was previously unloaded and re-added to visual tree
        RegisterMessenger();
        UpdateUI();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void OnPlaybackStateChanged(PlaybackStateChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _isPlaying = e.State == PlaybackState.Playing;
            UpdateUI();
        });
    }

    private void OnPositionChanged(TimeSpan position)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var duration = _playbackService.Duration;
            if (duration.TotalSeconds > 0)
            {
                ProgressBar.Value = position.TotalSeconds / duration.TotalSeconds * 100;
            }
        });
    }

    private void UpdateUI()
    {
        var audioBook = _playbackService.CurrentAudioBook;

        if (audioBook != null)
        {
            TitleText.Text = audioBook.Title;
            AuthorText.Text = audioBook.Author;

            CoverImage.Source = CoverImageService.LoadCover(audioBook.CoverPath, 160, 160);
        }
        else
        {
            TitleText.Text = _localization.GetString("NoBookPlaying");
            AuthorText.Text = "";
            CoverImage.Source = null;
            ProgressBar.Value = 0;
        }

        PlayPauseIcon.Glyph = _isPlaying ? "\uE769" : "\uE768";
    }

    private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            await _playbackService.PauseAsync();
        }
        else
        {
            await _playbackService.PlayAsync();
        }
    }

    private async void BtnRewind_Click(object sender, RoutedEventArgs e)
    {
        var newPosition = _playbackService.Position - TimeSpan.FromSeconds(10);
        if (newPosition < TimeSpan.Zero)
            newPosition = TimeSpan.Zero;
        await _playbackService.SeekAsync(newPosition);
    }

    private async void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        var newPosition = _playbackService.Position + TimeSpan.FromSeconds(30);
        if (newPosition > _playbackService.Duration)
            newPosition = _playbackService.Duration;
        await _playbackService.SeekAsync(newPosition);
    }
}
