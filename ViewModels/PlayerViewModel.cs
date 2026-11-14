using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using NineLivesAudio.Helpers;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;

namespace NineLivesAudio.ViewModels;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly IAudioPlaybackService _playbackService;
    private readonly ISettingsService _settingsService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private AudioBook? _currentAudioBook;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private TimeSpan _currentPosition;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private float _volume;

    [ObservableProperty]
    private float _playbackSpeed;

    [ObservableProperty]
    private string _currentPositionText = "00:00:00";

    [ObservableProperty]
    private string _durationText = "00:00:00";

    [ObservableProperty]
    private string _remainingTimeText = "00:00:00";

    [ObservableProperty]
    private int? _sleepTimerMinutes;

    [ObservableProperty]
    private string _sleepTimerText = string.Empty;

    private System.Timers.Timer? _sleepTimer;
    private DateTime? _sleepTimerEndTime;

    public PlayerViewModel(
        IAudioPlaybackService playbackService,
        ISettingsService settingsService,
        ILocalizationService localization)
    {
        _playbackService = playbackService;
        _settingsService = settingsService;
        _localization = localization;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        Volume = _playbackService.Volume;
        PlaybackSpeed = _playbackService.PlaybackSpeed;

        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
            ((PlayerViewModel)r).OnPlaybackStateChanged(m.Value));
        WeakReferenceMessenger.Default.Register<PositionChangedMessage>(this, (r, m) =>
            ((PlayerViewModel)r).OnPositionChanged(m.Value));
    }

    public void Initialize()
    {
        CurrentAudioBook = _playbackService.CurrentAudioBook;
        IsPlaying = _playbackService.State == PlaybackState.Playing;
        IsLoading = _playbackService.State == PlaybackState.Loading;
        CurrentPosition = _playbackService.Position;
        Duration = _playbackService.Duration;
        UpdateTimeTexts();
    }

    private void OnPlaybackStateChanged(PlaybackStateChangedEventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            IsPlaying = e.State == PlaybackState.Playing;
            IsLoading = e.State == PlaybackState.Loading;
            CurrentAudioBook = _playbackService.CurrentAudioBook;
            Duration = _playbackService.Duration;
            UpdateTimeTexts();
        });
    }

    private void OnPositionChanged(TimeSpan position)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            CurrentPosition = position;
            if (Duration.TotalSeconds > 0)
            {
                Progress = position.TotalSeconds / Duration.TotalSeconds * 100;
            }
            UpdateTimeTexts();
        });
    }

    private void UpdateTimeTexts()
    {
        CurrentPositionText = TimeFormatHelper.FormatTimeSpan(CurrentPosition);
        DurationText = TimeFormatHelper.FormatTimeSpan(Duration);

        var remaining = Duration - CurrentPosition;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        RemainingTimeText = $"-{TimeFormatHelper.FormatTimeSpan(remaining)}";
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
    private async Task StopAsync()
    {
        await _playbackService.StopAsync();
        CancelSleepTimer();
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
    private async Task SeekAsync(double progressPercent)
    {
        var newPosition = TimeSpan.FromSeconds(Duration.TotalSeconds * progressPercent / 100);
        await _playbackService.SeekAsync(newPosition);
    }

    [RelayCommand]
    private async Task SetVolumeAsync(float volume)
    {
        Volume = volume;
        await _playbackService.SetVolumeAsync(volume);
    }

    [RelayCommand]
    private async Task SetPlaybackSpeedAsync(float speed)
    {
        PlaybackSpeed = speed;
        await _playbackService.SetPlaybackSpeedAsync(speed);
    }

    [RelayCommand]
    private void SetSleepTimer(int? minutes)
    {
        CancelSleepTimer();

        if (!minutes.HasValue || minutes <= 0)
        {
            SleepTimerMinutes = null;
            SleepTimerText = string.Empty;
            return;
        }

        SleepTimerMinutes = minutes;
        _sleepTimerEndTime = DateTime.Now.AddMinutes(minutes.Value);

        _sleepTimer = new System.Timers.Timer(1000);
        _sleepTimer.Elapsed += OnSleepTimerTick;
        _sleepTimer.Start();

        UpdateSleepTimerText();
    }

    [RelayCommand]
    private void CancelSleepTimer()
    {
        _sleepTimer?.Stop();
        _sleepTimer?.Dispose();
        _sleepTimer = null;
        _sleepTimerEndTime = null;
        SleepTimerMinutes = null;
        SleepTimerText = string.Empty;
    }

    private void OnSleepTimerTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_sleepTimerEndTime == null) return;

        var remaining = _sleepTimerEndTime.Value - DateTime.Now;

        if (remaining <= TimeSpan.Zero)
        {
            // Timer expired, pause playback
            _ = _playbackService.PauseAsync();
            _dispatcherQueue.TryEnqueue(CancelSleepTimer);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(UpdateSleepTimerText);
        }
    }

    private void UpdateSleepTimerText()
    {
        if (_sleepTimerEndTime == null)
        {
            SleepTimerText = string.Empty;
            return;
        }

        var remaining = _sleepTimerEndTime.Value - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        SleepTimerText = _localization.Format("SleepIn", $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}");
    }

    partial void OnVolumeChanged(float value)
    {
        _ = _playbackService.SetVolumeAsync(value);
    }

    public void Dispose()
    {
        CancelSleepTimer();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    partial void OnPlaybackSpeedChanged(float value)
    {
        _ = _playbackService.SetPlaybackSpeedAsync(value);
    }
}
