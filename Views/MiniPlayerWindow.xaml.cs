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

        // The mini player is a separate Window: it needs the theme itself, otherwise it
        // stays dark while the main window is light.
        if (this.Content is Microsoft.UI.Xaml.FrameworkElement themeRoot)
            ThemeService.Register(themeRoot);

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

        if (_appWindow != null)
        {
            _appWindow.Changed += (s, args) =>
            {
                if (args.DidPresenterChange
                    && _appWindow.Presenter is OverlappedPresenter p
                    && p.State == OverlappedPresenterState.Minimized)
                {
                    var settings = App.Services.GetService<ISettingsService>()?.Settings;
                    if (settings?.MinimizeToTray == true)
                    {
                        App.Services.GetService<ITrayIconService>()?.Show();
                        _appWindow.Hide();
                    }
                }
            };
        }

        // Title bar colours follow the current palette (see ThemeService).
        ThemeService.RegisterTitleBar(_appWindow, hwnd);

        // Subscribe to playback events via Messenger
        WeakReferenceMessenger.Default.Register<PositionChangedMessage>(this, (r, m) =>
            ((MiniPlayerWindow)r).PlaybackService_PositionChanged(m.Value));
        WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
            ((MiniPlayerWindow)r).PlaybackService_StateChanged(m.Value));

        // Clean up on close
        this.Closed += OnClosed;

        // Pin colour is painted from code, so it has to be repainted on theme changes.
        ThemeService.PaletteChanged += OnPaletteChanged;

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

    private void OnPaletteChanged()
    {
        var window = this.Content as Microsoft.UI.Xaml.FrameworkElement;
        window?.DispatcherQueue.TryEnqueue(() =>
        {
            try { UpdatePinVisual(); }
            catch { /* window closing */ }
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ThemeService.PaletteChanged -= OnPaletteChanged;
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

            var settings = App.Services.GetService<ISettingsService>()?.Settings;
            var ch = _playbackService.CurrentChapter;
            if (settings?.PlayerChapterMode == true && ch != null && ch.End > ch.Start)
            {
                var chDur = TimeSpan.FromSeconds(ch.End - ch.Start);
                var chPos = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds - ch.Start, 0, chDur.TotalSeconds));
                ProgressSlider.Value = chPos.TotalSeconds / chDur.TotalSeconds * 100;
                CurrentTimeText.Text = TimeFormatHelper.FormatTimeSpan(chPos);
                var remaining = chDur - chPos;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                RemainingTimeText.Text = $"-{TimeFormatHelper.FormatTimeSpan(remaining)}";
            }
            else
            {
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

    public void RestoreFromTray()
    {
        _appWindow?.Show();
        Activate();
    }

    private void PinToggle_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;

        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _isPinned;
        }

        UpdatePinVisual();
        _logger.LogDebug($"[MiniPlayer] Always on top: {_isPinned}");
    }

    private void UpdatePinVisual()
    {
        PinIcon.Glyph = _isPinned ? "\uE840" : "\uE718"; // Pinned vs UnPin
        PinIcon.Foreground = _isPinned
            ? ThemeService.Brush("SigilGoldBrush")
            : ThemeService.Brush("MistFaintBrush");
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
