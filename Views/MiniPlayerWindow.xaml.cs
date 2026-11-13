using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Helpers;
using NineLivesAudio.Messages;
using NineLivesAudio.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Runtime.InteropServices;

namespace NineLivesAudio.Views;

public sealed partial class MiniPlayerWindow : Window
{
    private readonly IAudioPlaybackService _playbackService;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;
    private AppWindow? _appWindow;
    private bool _isUserSeeking;
    private bool _isPinned;

    public MiniPlayerWindow()
    {
        _playbackService = App.Services.GetRequiredService<IAudioPlaybackService>();
        _logger = App.Services.GetRequiredService<ILoggingService>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();

        this.InitializeComponent();

        Title = "Nine Lives Audio";

        // Set up compact window size and title bar styling
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow?.Resize(new Windows.Graphics.SizeInt32(360, 218));
        try
        {
            var windowIcon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            if (System.IO.File.Exists(windowIcon)) _appWindow?.SetIcon(windowIcon);
        }
        catch { /* Non-fatal — icon is cosmetic */ }

        // Set title bar icon via Win32
        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            if (System.IO.File.Exists(icoPath))
            {
                var smallIcon = LoadImage(IntPtr.Zero, icoPath, 1, 16, 16, 0x0010);
                var largeIcon = LoadImage(IntPtr.Zero, icoPath, 1, 32, 32, 0x0010);
                if (smallIcon != IntPtr.Zero) SendMessage(hwnd, 0x0080, (IntPtr)0, smallIcon);
                if (largeIcon != IntPtr.Zero) SendMessage(hwnd, 0x0080, (IntPtr)1, largeIcon);
            }
        }
        catch { /* Non-fatal */ }

        // Dark title bar matching the app theme
        if (_appWindow?.TitleBar is { } titleBar)
        {
            titleBar.BackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10);
            titleBar.ForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE8);
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10);
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE8);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x11, 0x18, 0x27);
            titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x22, 0x36);
            titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10);
            titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10);
            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80);
        }

        // Subscribe to playback events via Messenger
        WeakReferenceMessenger.Default.Register<PositionChangedMessage>(this, (r, m) =>
            ((MiniPlayerWindow)r).PlaybackService_PositionChanged(m.Value));
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
            ((MiniPlayerWindow)r).PlaybackService_StateChanged(m.Value));

        // Clean up on close
        this.Closed += OnClosed;

        ApplyLocalization();
        _localization.LanguageChanged -= ApplyLocalization;
        _localization.LanguageChanged += ApplyLocalization;

        // Initialize UI from current state
        UpdatePlayPauseIcon();
        UpdateProgressFromCurrentState();

        _logger.Log("[MiniPlayer] Window opened");
    }

    private void ApplyLocalization()
    {
        if (PinToggle != null) ToolTipService.SetToolTip(PinToggle, _localization.GetString("KeepOnTop"));
        if (ExpandButton != null) ToolTipService.SetToolTip(ExpandButton, _localization.GetString("BackToFullPlayer"));
        if (SkipBackButton != null) ToolTipService.SetToolTip(SkipBackButton, _localization.GetString("SkipBackTip"));
        if (SkipForwardButton != null) ToolTipService.SetToolTip(SkipForwardButton, _localization.GetString("SkipForwardTip"));
        if (PlayPauseButton != null) ToolTipService.SetToolTip(PlayPauseButton, _localization.GetString("PlayPauseTip"));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _localization.LanguageChanged -= ApplyLocalization;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _logger.Log("[MiniPlayer] Window closed");
    }

    // --- Playback event handlers ---

    private void PlaybackService_PositionChanged(TimeSpan position)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isUserSeeking) return;

            var duration = _playbackService.Duration;
            if (duration.TotalSeconds > 0)
            {
                ProgressSlider.Value = position.TotalSeconds / duration.TotalSeconds * 100;
            }

            CurrentTimeText.Text = TimeFormatHelper.FormatTimeSpan(position);
            var remaining = duration - position;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            RemainingTimeText.Text = $"-{TimeFormatHelper.FormatTimeSpan(remaining)}";
        });
    }

    private void PlaybackService_StateChanged(PlaybackStateChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdatePlayPauseIcon();

            // Auto-close when playback stops
            if (e.State == PlaybackState.Stopped)
            {
                _logger.Log("[MiniPlayer] Playback stopped, closing mini-player");
                this.Close();
            }
        });
    }

    private void UpdatePlayPauseIcon()
    {
        var isPlaying = _playbackService.State == PlaybackState.Playing
                     || _playbackService.State == PlaybackState.Buffering;
        PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";
    }

    private void UpdateProgressFromCurrentState()
    {
        var position = _playbackService.Position;
        var duration = _playbackService.Duration;
        if (duration.TotalSeconds > 0)
        {
            ProgressSlider.Value = position.TotalSeconds / duration.TotalSeconds * 100;
        }

        CurrentTimeText.Text = TimeFormatHelper.FormatTimeSpan(position);
        var remaining = duration - position;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        RemainingTimeText.Text = $"-{TimeFormatHelper.FormatTimeSpan(remaining)}";
    }

    // --- Button handlers ---

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_playbackService.State == PlaybackState.Playing)
            await _playbackService.PauseAsync();
        else
            await _playbackService.PlayAsync();
    }

    private async void SkipBack_Click(object sender, RoutedEventArgs e)
    {
        var target = _playbackService.Position - TimeSpan.FromSeconds(10);
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        await _playbackService.SeekAsync(target);
    }

    private async void SkipForward_Click(object sender, RoutedEventArgs e)
    {
        var target = _playbackService.Position + TimeSpan.FromSeconds(30);
        if (target > _playbackService.Duration) target = _playbackService.Duration;
        await _playbackService.SeekAsync(target);
    }

    private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.FocusState != FocusState.Unfocused)
        {
            _isUserSeeking = true;
            var duration = _playbackService.Duration;
            if (duration.TotalSeconds > 0)
            {
                var seekPosition = TimeSpan.FromSeconds(duration.TotalSeconds * e.NewValue / 100);
                _ = _playbackService.SeekAsync(seekPosition);
            }
            _isUserSeeking = false;
        }
    }

    // --- Pin / Expand ---

    private void PinToggle_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;

        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _isPinned;
        }

        // Update pin icon appearance
        PinIcon.Glyph = _isPinned ? "\uE840" : "\uE718"; // Pinned vs UnPin
        PinIcon.Foreground = _isPinned
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SigilGoldBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MistFaintBrush"];

        _logger.LogDebug($"[MiniPlayer] Always on top: {_isPinned}");
    }

    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        _logger.Log("[MiniPlayer] Expanding to full player");

        // Activate the main window
        try
        {
            var mainWindow = App.Services.GetRequiredService<MainWindow>();
            mainWindow.Activate();
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[MiniPlayer] Failed to activate main window: {ex.Message}");
        }

        this.Close();
    }

    // Win32 imports for title bar icon
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType,
        int cxDesired, int cyDesired, uint fuLoad);
}
