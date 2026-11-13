using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Messages;
using NineLivesAudio.Services;
using NineLivesAudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace NineLivesAudio
{
    public sealed partial class MainWindow : Window
    {
        private readonly IAppInitializer _initializer;
        private readonly ILoggingService _logger;
        private readonly IAudioPlaybackService _playbackService;
        private readonly INotificationService _notifications;
        private readonly IMetadataNormalizer _normalizer;
        private readonly IConnectivityService _connectivity;
        private readonly INavigationService _navigationService;
        private readonly MainViewModel _mainViewModel;
        private readonly ILocalizationService _localization;
        private readonly ITrayIconService _tray;
        private DateTime _lastMiniPlayerUpdate = DateTime.MinValue;

        // Window reference for preset sizing
        private Microsoft.UI.Windowing.AppWindow? _appWindow;

        public MainWindow()
        {
            this.InitializeComponent();

            Title = "Nine Lives Audio";

            // Set initial window size (portrait default) — user can freely resize/maximize
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            _appWindow?.Resize(new Windows.Graphics.SizeInt32(550, 660)); // 10% larger than minimum
            try
            {
                var windowIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
                if (File.Exists(windowIcon)) _appWindow?.SetIcon(windowIcon); // Taskbar + title bar icon
            }
            catch { /* Non-fatal — icon is cosmetic */ }

            // Set title bar icon via Win32 (SetIcon doesn't reliably set the small icon)
            try
            {
                var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
                if (File.Exists(icoPath))
                {
                    var smallIcon = LoadImage(IntPtr.Zero, icoPath, 1 /*IMAGE_ICON*/, 16, 16, 0x0010 /*LR_LOADFROMFILE*/);
                    var largeIcon = LoadImage(IntPtr.Zero, icoPath, 1, 32, 32, 0x0010);
                    if (smallIcon != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, smallIcon);
                    if (largeIcon != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, largeIcon);
                }
            }
            catch { /* Non-fatal — icon is cosmetic */ }

            // Color title bar to match dark void theme
            if (_appWindow?.TitleBar is { } titleBar)
            {
                // Active window
                titleBar.BackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10); // VoidDeep
                titleBar.ForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE8); // StarlightDim
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10);
                titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE8);
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x11, 0x18, 0x27); // VoidSurface
                titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x22, 0x36); // VoidElevated
                titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
                // Inactive window
                titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10);
                titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80); // MistFaint
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10);
                titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80);
            }

            // Enforce minimum size only — no aspect ratio enforcement, no blocking maximize
            SetMinimumWindowSize(hwnd, 500, 600);

            _initializer = App.Services.GetRequiredService<IAppInitializer>();
            _logger = App.Services.GetRequiredService<ILoggingService>();
            _playbackService = App.Services.GetRequiredService<IAudioPlaybackService>();
            _notifications = App.Services.GetRequiredService<INotificationService>();
            _normalizer = App.Services.GetRequiredService<IMetadataNormalizer>();
            _connectivity = App.Services.GetRequiredService<IConnectivityService>();
            _navigationService = App.Services.GetRequiredService<INavigationService>();
            _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
            _localization = App.Services.GetRequiredService<ILocalizationService>();
            this.Closed += MainWindow_Closed;

            // Minimize-to-tray: the service swallows WM_SYSCOMMAND/SC_MINIMIZE while the
            // setting is on, hides the window and keeps a notification-area icon around.
            _tray = App.Services.GetRequiredService<ITrayIconService>();
            _tray.Attach(this);

            // Wire MiniPlayer to playback events via Messenger
            WeakReferenceMessenger.Default.Register<PlaybackStateChangedMessage>(this, (r, m) =>
                ((MainWindow)r).OnPlaybackStateChanged(m.Value));
            WeakReferenceMessenger.Default.Register<PositionChangedMessage>(this, (r, m) =>
                ((MainWindow)r).OnPositionChanged(m.Value));

            // Wire notification service via Messenger
            WeakReferenceMessenger.Default.Register<NotificationRequestedMessage>(this, (r, m) =>
            {
                ((MainWindow)r).OnNotificationRequested(m.Value);
            });

            // Wire connectivity monitoring via Messenger
            WeakReferenceMessenger.Default.Register<ConnectivityChangedMessage>(this, (r, m) =>
            {
                ((MainWindow)r).OnConnectivityChanged(m.Value);
            });

            // Kick off async init after the window content is loaded
            if (this.Content is FrameworkElement rootElement)
                rootElement.Loaded += OnContentLoaded;
        }

        private async void OnContentLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el)
                el.Loaded -= OnContentLoaded; // Only once
            await RunInitializationAsync();
        }

        private async Task RunInitializationAsync()
        {
            try
            {
                InitOverlay.Visibility = Visibility.Visible;
                InitErrorPanel.Visibility = Visibility.Collapsed;
                AppContent.Visibility = Visibility.Collapsed;
                InitStatusText.Text = _localization.GetString("Initializing");

                await _initializer.InitializeAsync();

                if (_initializer.State == InitState.Ready)
                {
                    ShowApp();
                }
                else
                {
                    ShowInitError(_initializer.ErrorMessage ?? _localization.GetString("UnknownInitError"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("MainWindow init failed", ex);
                ShowInitError(ex.Message);
            }
        }

        private void ShowApp()
        {
            ApplyLocalization();
            _localization.LanguageChanged -= ApplyLocalization;
            _localization.LanguageChanged += ApplyLocalization;
            InitOverlay.Visibility = Visibility.Collapsed;
            InitErrorPanel.Visibility = Visibility.Collapsed;
            AppContent.Visibility = Visibility.Visible;

            // Initialize navigation service with frame and nav view
            _navigationService.Initialize(ContentFrame, NavView);

            NavView.SelectedItem = NavView.MenuItems[0]; // Home
            NavigateToPage("Home");

            // Start connectivity monitoring
            _ = _connectivity.StartMonitoringAsync();
            UpdateConnectivityUI(_connectivity.IsOnline, _connectivity.IsServerReachable);
        }

        private void ShowInitError(string message)
        {
            InitOverlay.Visibility = Visibility.Collapsed;
            InitErrorPanel.Visibility = Visibility.Visible;
            AppContent.Visibility = Visibility.Collapsed;
            InitErrorText.Text = message;
        }

        private async void RetryInit_Click(object sender, RoutedEventArgs e)
        {
            await RunInitializationAsync();
        }

        private void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NineLivesAudio", "Logs");
                Process.Start(new ProcessStartInfo { FileName = logDir, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to open logs folder", ex);
            }
        }

        private async void ResetConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = App.Services.GetRequiredService<ISettingsService>();
                await settings.ClearAuthTokenAsync();
                settings.Settings.ServerUrl = string.Empty;
                settings.Settings.Username = string.Empty;
                await settings.SaveSettingsAsync();
                _logger.Log("Connection reset by user from init error screen");
                await RunInitializationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Reset connection failed", ex);
            }
        }

        // --- Connectivity ---

        private void OnConnectivityChanged(ConnectivityChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() => UpdateConnectivityUI(e.IsOnline, e.IsServerReachable));
        }

        private void UpdateConnectivityUI(bool isOnline, bool isServerReachable)
        {
            // Always use a solid circle dot — color indicates status
            ConnectivityIcon.Glyph = "\u25CF"; // ● solid circle
            ConnectivityIcon.Opacity = 1.0;

            if (!isOnline)
            {
                ConnectivityIcon.Foreground = (Brush)Application.Current.Resources["RitualErrorBrush"];
                ConnectivityText.Text = _localization.GetString("Offline");
                ConnectivityText.Foreground = (Brush)Application.Current.Resources["MistFaintBrush"];
            }
            else if (!isServerReachable)
            {
                ConnectivityIcon.Foreground = (Brush)Application.Current.Resources["RitualWarningBrush"];
                ConnectivityText.Text = _localization.GetString("ServerUnreachable");
                ConnectivityText.Foreground = (Brush)Application.Current.Resources["MistFaintBrush"];
            }
            else
            {
                ConnectivityIcon.Foreground = (Brush)Application.Current.Resources["RitualSuccessBrush"];
                ConnectivityText.Text = _localization.GetString("Connected");
                ConnectivityText.Foreground = (Brush)Application.Current.Resources["StarlightBrush"];
            }
        }

        // --- Window Sizing: Minimum size via Win32 WM_GETMINMAXINFO hook ---

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int GWLP_WNDPROC = -4;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private static WndProcDelegate? _newWndProc; // prevent GC of delegate
        private static IntPtr _oldWndProc;
        private static int _minWidthPx;
        private static int _minHeightPx;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Title bar icon via Win32
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType,
            int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        /// <summary>
        /// Enforce a minimum window size via Win32 subclass hook.
        /// Accounts for DPI scaling (WM_GETMINMAXINFO uses physical pixels).
        /// </summary>
        private static void SetMinimumWindowSize(IntPtr hwnd, int minWidth, int minHeight)
        {
            var dpi = GetDpiForWindow(hwnd);
            var scale = dpi / 96.0;
            _minWidthPx = (int)(minWidth * scale);
            _minHeightPx = (int)(minHeight * scale);

            _newWndProc = new WndProcDelegate(MinSizeWndProc);
            _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_newWndProc));
        }

        private static IntPtr MinSizeWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.X = _minWidthPx;
                info.ptMinTrackSize.Y = _minHeightPx;
                Marshal.StructureToPtr(info, lParam, false);
                return IntPtr.Zero;
            }
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        // --- Navigation ---

        private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            _navigationService.GoBack();
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                UpdateNavGlow(null);
                NavigateToPage("Settings");
            }
            else if (args.SelectedItemContainer is NavigationViewItem selectedItem)
            {
                UpdateNavGlow(selectedItem);
                var tag = selectedItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(tag))
                    NavigateToPage(tag);
            }
        }

        /// <summary>
        /// Apply faint gold background glow to the selected navigation item.
        /// </summary>
        private void UpdateNavGlow(NavigationViewItem? selected)
        {
            foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
                item.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            if (selected != null)
                selected.Background = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x1A, 0xC5, 0xA5, 0x5A)); // SigilGoldGlow
        }

        private void NavigateToPage(string pageTag)
        {
            Type? pageType = pageTag switch
            {
                "Home" => typeof(Views.HomePage),
                "Library" => typeof(Views.LibraryPage),
                "Player" => typeof(Views.PlayerPage),
                "Downloads" => typeof(Views.DownloadsPage),
                "Settings" => typeof(Views.SettingsPage),
                _ => null
            };

            if (pageType != null)
                _navigationService.NavigateTo(pageType);
        }

        // --- MiniPlayer wiring ---

        private void OnPlaybackStateChanged(PlaybackStateChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                var book = _playbackService.CurrentAudioBook;
                bool showMini = book != null && e.State != PlaybackState.Stopped;

                MiniPlayerBar.Visibility = showMini ? Visibility.Visible : Visibility.Collapsed;

                if (book != null)
                {
                    // Use normalized metadata for display
                    var normalized = _normalizer.Normalize(book);
                    MiniPlayerTitle.Text = normalized.DisplayTitle;
                    MiniPlayerAuthor.Text = normalized.DisplayAuthor;
                    MiniPlayPauseIcon.Glyph = e.State == PlaybackState.Playing ? "\uE769" : "\uE768";

                    // Gold play icon when playing, default when paused
                    MiniPlayPauseIcon.Foreground = e.State == PlaybackState.Playing
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC5, 0xA5, 0x5A)) // SigilGold
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE8)); // StarlightDim

                    MiniPlayerArt.Source = CoverImageService.LoadThumbnail(book.CoverPath);
                }
            });
        }

        private void OnPositionChanged(TimeSpan position)
        {
            // Throttle mini player updates to ~4/sec
            var now = DateTime.UtcNow;
            if ((now - _lastMiniPlayerUpdate).TotalMilliseconds < 250)
                return;
            _lastMiniPlayerUpdate = now;

            DispatcherQueue.TryEnqueue(() =>
            {
                var duration = _playbackService.Duration;
                if (duration.TotalSeconds > 0)
                    MiniPlayerProgress.Value = position.TotalSeconds / duration.TotalSeconds * 100;
            });
        }

        private void MiniPlayer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(sender as UIElement);
            if (point.Properties.IsLeftButtonPressed)
            {
                if (NavView.MenuItems.Count > 2)
                    NavView.SelectedItem = NavView.MenuItems[2]; // Player
                _navigationService.NavigateTo(typeof(Views.PlayerPage));
            }
        }

        private async void MiniPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackService.State == PlaybackState.Playing)
                await _playbackService.PauseAsync();
            else
                await _playbackService.PlayAsync();
        }

        private async void MiniRewind_Click(object sender, RoutedEventArgs e)
        {
            var pos = _playbackService.Position - TimeSpan.FromSeconds(10);
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            await _playbackService.SeekAsync(pos);
        }

        private async void MiniForward_Click(object sender, RoutedEventArgs e)
        {
            var pos = _playbackService.Position + TimeSpan.FromSeconds(30);
            if (pos > _playbackService.Duration) pos = _playbackService.Duration;
            await _playbackService.SeekAsync(pos);
        }

        // --- Notification handling ---

        private void OnNotificationRequested(NotificationEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (e.ShouldDismiss)
                {
                    AppNotification.IsOpen = false;
                    return;
                }

                AppNotification.Title = e.Title ?? string.Empty;
                AppNotification.Message = e.Message;
                AppNotification.Severity = e.Type switch
                {
                    NotificationType.Success => InfoBarSeverity.Success,
                    NotificationType.Error => InfoBarSeverity.Error,
                    NotificationType.Warning => InfoBarSeverity.Warning,
                    _ => InfoBarSeverity.Informational
                };
                AppNotification.IsOpen = true;
            });
        }

        private void AppNotification_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            // Auto-closed or user closed
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            this.Closed -= MainWindow_Closed;

            // Never leave an icon behind in the notification area.
            _tray?.Dispose();

            if (_mainViewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        private void ApplyLocalization()
        {
            if (NavHomeItem != null) NavHomeItem.Content = _localization.GetString("NavHome");
            if (NavLibraryItem != null) NavLibraryItem.Content = _localization.GetString("NavLibrary");
            if (NavPlayerItem != null) NavPlayerItem.Content = _localization.GetString("NavPlayer");
            if (NavDownloadsItem != null) NavDownloadsItem.Content = _localization.GetString("NavDownloads");
            if (RetryButton != null) RetryButton.Content = _localization.GetString("Retry");
            if (OpenLogsButton != null) OpenLogsButton.Content = _localization.GetString("OpenLogs");
            if (ResetButton != null) ResetButton.Content = _localization.GetString("ResetConnection");

            // Mini-player bar tooltips
            if (MiniRewindBtn != null) ToolTipService.SetToolTip(MiniRewindBtn, _localization.GetString("SkipBackTip"));
            if (MiniForwardBtn != null) ToolTipService.SetToolTip(MiniForwardBtn, _localization.GetString("SkipForwardTip"));
            if (MiniPlayPauseBtn != null) ToolTipService.SetToolTip(MiniPlayPauseBtn, _localization.GetString("PlayPauseTip"));
            // Re-render the status dot text in the new language.
            UpdateConnectivityUI(_connectivity.IsOnline, _connectivity.IsServerReachable);
        }

    }
}
