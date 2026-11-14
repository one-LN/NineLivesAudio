using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace NineLivesAudio.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlaybackService _playbackService;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ISyncService _syncService;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isMiniPlayerVisible;

    [ObservableProperty]
    private AudioBook? _currentAudioBook;

    [ObservableProperty]
    private TimeSpan _currentPosition;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private bool _isSyncing;

    public MainViewModel(
        IAudioPlaybackService playbackService,
        IAudioBookshelfApiService apiService,
        ISettingsService settingsService,
        ISyncService syncService)
    {
        _playbackService = playbackService;
        _apiService = apiService;
        _settingsService = settingsService;
        _syncService = syncService;

        // Subscribe to playback events via Messenger
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
            ((MainViewModel)r).OnPlaybackStateChanged(m.Value));
        WeakReferenceMessenger.Default.Register<PositionChangedMessage>(this, (r, m) =>
            ((MainViewModel)r).OnPositionChanged(m.Value));

        // Subscribe to sync events via Messenger
        WeakReferenceMessenger.Default.Register<SyncStartedMessage>(this, (r, m) =>
            ((MainViewModel)r).IsSyncing = true);
        WeakReferenceMessenger.Default.Register<SyncCompletedMessage>(this, (r, m) =>
        {
            var vm = (MainViewModel)r;
            vm.IsSyncing = false;
            // A completed sync means the credentials work: a server added on this run of the
            // app must flip the shell out of its "not connected" state, not on next restart.
            vm.IsAuthenticated = vm._apiService.IsAuthenticated;
        });
        WeakReferenceMessenger.Default.Register<SyncFailedMessage>(this, (r, m) =>
            ((MainViewModel)r).IsSyncing = false);

        IsAuthenticated = _apiService.IsAuthenticated;
    }

    private void OnPlaybackStateChanged(PlaybackStateChangedEventArgs e)
    {
        IsPlaying = e.State == PlaybackState.Playing;
        CurrentAudioBook = _playbackService.CurrentAudioBook;
        IsMiniPlayerVisible = CurrentAudioBook != null;
        Duration = _playbackService.Duration;
    }

    private void OnPositionChanged(TimeSpan position)
    {
        CurrentPosition = position;
        if (Duration.TotalSeconds > 0)
        {
            Progress = position.TotalSeconds / Duration.TotalSeconds * 100;
        }
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        if (IsPlaying)
        {
            await _playbackService.PauseAsync();
        }
        else
        {
            await _playbackService.PlayAsync();
        }
    }

    [RelayCommand]
    private async Task SkipForwardAsync()
    {
        var newPosition = CurrentPosition + TimeSpan.FromSeconds(30);
        if (newPosition > Duration)
            newPosition = Duration;
        await _playbackService.SeekAsync(newPosition);
    }

    [RelayCommand]
    private async Task SkipBackwardAsync()
    {
        var newPosition = CurrentPosition - TimeSpan.FromSeconds(10);
        if (newPosition < TimeSpan.Zero)
            newPosition = TimeSpan.Zero;
        await _playbackService.SeekAsync(newPosition);
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        await _syncService.SyncNowAsync();
    }
}
