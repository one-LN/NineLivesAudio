using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace NineLivesAudio.Services;

/// <summary>
/// Notification-area icon built on <c>Shell_NotifyIcon</c>. WinUI 3 has no tray support of
/// its own, and the alternative (a NuGet wrapper) would add a dependency for ~200 lines of
/// well-known interop.
/// <para>
/// The icon is only visible while the window is hidden: minimizing intercepts
/// <c>WM_SYSCOMMAND / SC_MINIMIZE</c>, hides the window and shows the icon; restoring hides
/// it again so the notification area never collects stale entries.
/// </para>
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAY_CALLBACK = WM_APP + 1;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_NULL = 0x0000;

    private const uint SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint MF_STRING = 0x00000000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_LEFTALIGN = 0x0000;

    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    private const int MenuRestore = 1;
    private const int MenuPlayPause = 2;
    private const int MenuExit = 3;

    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;
    private readonly IAudioPlaybackService _playback;

    private readonly SubclassProc _subclassProc;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _iconHandle = IntPtr.Zero;
    private Window? _window;
    private bool _attached;

    public bool IsShown { get; private set; }

    public TrayIconService(
        ILoggingService logger,
        ILocalizationService localization,
        ISettingsService settings,
        IAudioPlaybackService playback)
    {
        _logger = logger;
        _localization = localization;
        _settings = settings;
        _playback = playback;

        // Held in a field: the native side keeps the raw function pointer, so a collected
        // delegate would crash the moment the window receives a message.
        _subclassProc = WindowSubclassProc;
    }

    public void Attach(Window window)
    {
        if (_attached) return;

        _window = window;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (_hwnd == IntPtr.Zero)
        {
            _logger.LogWarning("[Tray] Could not obtain the window handle; minimize-to-tray is off");
            return;
        }

        if (!SetWindowSubclass(_hwnd, _subclassProc, TraySubclassId, IntPtr.Zero))
        {
            _logger.LogWarning("[Tray] SetWindowSubclass failed; minimize-to-tray is off");
            return;
        }

        _attached = true;
        _logger.Log("[Tray] Attached to the main window");
    }

    public void Show()
    {
        if (IsShown || _hwnd == IntPtr.Zero) return;

        var data = CreateIconData();
        if (!Shell_NotifyIcon(NIM_ADD, ref data))
        {
            _logger.LogWarning("[Tray] Shell_NotifyIcon(NIM_ADD) failed");
            return;
        }

        IsShown = true;
        _logger.Log("[Tray] Icon added");
    }

    public void Hide()
    {
        if (!IsShown) return;

        var data = CreateIconData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        IsShown = false;
        _logger.Log("[Tray] Icon removed");
    }

    public void Dispose()
    {
        Hide();

        if (_attached && _hwnd != IntPtr.Zero)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, TraySubclassId);
            _attached = false;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    // ── window messages ──────────────────────────────────────────────────

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        try
        {
            if (uMsg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_MINIMIZE)
            {
                // Read the setting on every minimize so toggling it takes effect at once.
                if (_settings.Settings.MinimizeToTray)
                {
                    HideToTray();
                    return IntPtr.Zero; // swallow: the window must not appear in the taskbar
                }
            }
            else if (uMsg == WM_TRAY_CALLBACK)
            {
                var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMessage is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
                {
                    RestoreFromTray();
                    return IntPtr.Zero;
                }

                if (mouseMessage == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                    return IntPtr.Zero;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Tray] Message 0x{uMsg:X} failed: {ex.Message}");
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void HideToTray()
    {
        Show();
        ShowWindow(_hwnd, SW_HIDE);
        _logger.Log("[Tray] Minimized to the notification area");
    }

    private void RestoreFromTray()
    {
        ShowWindow(_hwnd, SW_SHOW);
        ShowWindow(_hwnd, SW_RESTORE);
        SetForegroundWindow(_hwnd);
        _window?.Activate();
        Hide();
        _logger.Log("[Tray] Restored from the notification area");
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            var isPlaying = _playback.State == PlaybackState.Playing;
            var playPauseLabel = isPlaying
                ? _localization.GetString("Pause")
                : _localization.GetString("Play");

            AppendMenuW(menu, MF_STRING, (UIntPtr)MenuRestore, _localization.GetString("TrayShow"));
            AppendMenuW(menu, MF_STRING, (UIntPtr)MenuPlayPause, playPauseLabel);
            AppendMenuW(menu, 0x00000800 /*MF_SEPARATOR*/, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)MenuExit, _localization.GetString("TrayExit"));

            GetCursorPos(out var cursor);

            // Without SetForegroundWindow the menu does not dismiss when clicking elsewhere.
            SetForegroundWindow(_hwnd);

            var command = TrackPopupMenu(
                menu,
                TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN,
                cursor.X,
                cursor.Y,
                0,
                _hwnd,
                IntPtr.Zero);

            // Documented workaround for the "menu stays up" bug.
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case MenuRestore:
                    RestoreFromTray();
                    break;
                case MenuPlayPause:
                    _ = TogglePlaybackAsync();
                    break;
                case MenuExit:
                    _logger.Log("[Tray] Exit requested from the notification area");
                    _window?.Close();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private async Task TogglePlaybackAsync()
    {
        try
        {
            if (_playback.State == PlaybackState.Playing)
                await _playback.PauseAsync();
            else
                await _playback.PlayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Tray] Play/pause failed: {ex.Message}");
        }
    }

    // ── icon plumbing ────────────────────────────────────────────────────

    private NOTIFYICONDATA CreateIconData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = GetIconHandle(),
            szTip = "Nine Lives Audio",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private IntPtr GetIconHandle()
    {
        if (_iconHandle != IntPtr.Zero) return _iconHandle;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            if (File.Exists(iconPath))
            {
                _iconHandle = LoadImage(
                    IntPtr.Zero,
                    iconPath,
                    IMAGE_ICON,
                    GetSystemMetrics(SM_CXSMICON),
                    GetSystemMetrics(SM_CYSMICON),
                    LR_LOADFROMFILE);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Tray] Icon load failed: {ex.Message}");
        }

        if (_iconHandle == IntPtr.Zero)
            _iconHandle = LoadIcon(IntPtr.Zero, 32512 /*IDI_APPLICATION*/);

        return _iconHandle;
    }

    private static readonly UIntPtr TraySubclassId = 0x4E4C;
    private static readonly uint TrayIconId = 1;

    // ── interop ──────────────────────────────────────────────────────────

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, int lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
