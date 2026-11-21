using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace NineLivesAudio.Helpers;

/// <summary>
/// Applies the "Appearance → Theme" setting to the live windows and hands out palette brushes.
/// <para>
/// Every colour in <c>Resources/Styles.xaml</c> lives in a <c>ThemeDictionaries</c> entry
/// ("Default" = the cosmic dark look, "Light" = daylight), and views reference them with
/// <c>{ThemeResource}</c>, so switching <see cref="ElementTheme"/> on the window root repaints
/// the whole UI without a restart. Code that needs a brush at runtime goes through
/// <see cref="Brush"/> instead of <c>Application.Current.Resources[…]</c>, which cannot see
/// inside a theme dictionary.
/// </para>
/// </summary>
public static class ThemeService
{
    private static readonly List<WeakReference<FrameworkElement>> Roots = new();
    private static readonly List<WeakReference<Microsoft.UI.Windowing.AppWindow>> TitleBars = new();

    /// <summary>AppWindow id → HWND, so the DWM caption can be repainted with the palette.</summary>
    private static readonly Dictionary<ulong, IntPtr> TitleBarHandles = new();
    private static readonly object Gate = new();

    /// <summary>"system" (default) | "en-US"-style values are not used here: "System"/"Light"/"Dark".</summary>
    public static string Preference { get; private set; } = "System";

    /// <summary>True when the palette currently in use is the light one.</summary>
    public static bool IsLight { get; private set; }

    /// <summary>
    /// Raised after the palette changed. Views that paint colours from code (status dots,
    /// badges, pinned icons) re-run their painting here, because a brush handed out earlier
    /// is a snapshot of the old theme — <c>{ThemeResource}</c> only re-resolves in XAML.
    /// </summary>
    public static event Action? PaletteChanged;

    public static ElementTheme Resolve(string? theme) => theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    /// <summary>Remembers a window root so later theme changes reach it.</summary>
    public static void Register(FrameworkElement root)
    {
        if (root == null) return;

        lock (Gate)
        {
            Roots.RemoveAll(r => !r.TryGetTarget(out _));
            if (!Roots.Any(r => r.TryGetTarget(out var existing) && ReferenceEquals(existing, root)))
                Roots.Add(new WeakReference<FrameworkElement>(root));
        }

        root.ActualThemeChanged += (sender, _) =>
        {
            IsLight = ((FrameworkElement)sender).ActualTheme == ElementTheme.Light;
        };

        root.RequestedTheme = Resolve(Preference);
        IsLight = root.ActualTheme == ElementTheme.Light;
    }

    /// <summary>
    /// The native title bar is drawn by DWM, not by XAML, so it has to be repainted by hand
    /// whenever the palette changes (a light window with a black caption looks broken).
    /// </summary>
    public static void RegisterTitleBar(Microsoft.UI.Windowing.AppWindow? appWindow, IntPtr hwnd = default)
    {
        if (appWindow == null) return;

        lock (Gate)
        {
            TitleBars.RemoveAll(r => !r.TryGetTarget(out _));
            TitleBars.Add(new WeakReference<Microsoft.UI.Windowing.AppWindow>(appWindow));
            if (hwnd != IntPtr.Zero) TitleBarHandles[appWindow.Id.Value] = hwnd;
        }

        PaintTitleBar(appWindow);
    }

    /// <summary>
    /// The title bar is drawn by DWM, not by XAML. The AppWindow.TitleBar colours below only
    /// cover Windows 11's customisable caption; on Windows 10 (and whenever the system caption
    /// is drawn in the OS theme) they are ignored, which left a white bar on top of a black
    /// window. These DWM attributes are the part that actually switches the caption.
    /// </summary>
    private static void PaintNativeCaption(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var light = IsLight;
        var dark = light ? 0 : 1;

        try
        {
            // 20 on Windows 10 20H1+, 19 on older builds; sending both is harmless.
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeLegacy, ref dark, sizeof(int));

            // Windows 11 22H2+: explicit colours so the caption matches the palette exactly.
            var caption = ToColorRef(light
                ? Windows.UI.Color.FromArgb(0xFF, 0xF6, 0xF7, 0xF9)
                : Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10));
            var text = ToColorRef(light
                ? Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x27, 0x35)
                : Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE8));

            DwmSetWindowAttribute(hwnd, DwmCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmTextColor, ref text, sizeof(int));
        }
        catch { /* older Windows — the AppWindow colours above are the fallback */ }
    }

    /// <summary>COLORREF is 0x00BBGGRR, the reverse of the usual RGB order.</summary>
    private static int ToColorRef(Windows.UI.Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void PaintTitleBar(Microsoft.UI.Windowing.AppWindow? appWindow)
    {
        if (appWindow == null) return;

        IntPtr hwnd;
        lock (Gate) { TitleBarHandles.TryGetValue(appWindow.Id.Value, out hwnd); }
        PaintNativeCaption(hwnd);

        if (appWindow.TitleBar is not { } titleBar) return;

        var light = IsLight;
        var background = light ? Windows.UI.Color.FromArgb(0xFF, 0xF6, 0xF7, 0xF9) : Windows.UI.Color.FromArgb(0xFF, 0x05, 0x08, 0x10);
        var foreground = light ? Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x27, 0x35) : Windows.UI.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE8);
        var hover = light ? Windows.UI.Color.FromArgb(0xFF, 0xEA, 0xED, 0xF2) : Windows.UI.Color.FromArgb(0xFF, 0x11, 0x18, 0x27);
        var pressed = light ? Windows.UI.Color.FromArgb(0xFF, 0xDD, 0xE2, 0xE9) : Windows.UI.Color.FromArgb(0xFF, 0x1A, 0x22, 0x36);
        var inactiveForeground = light ? Windows.UI.Color.FromArgb(0xFF, 0x7C, 0x86, 0x95) : Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80);

        try
        {
            titleBar.BackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.InactiveBackgroundColor = background;
            titleBar.InactiveForegroundColor = inactiveForeground;
            titleBar.ButtonInactiveBackgroundColor = background;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        }
        catch { /* title bar theming is cosmetic */ }
    }

    /// <summary>Applies a new preference ("System" / "Light" / "Dark") to every known window.</summary>
    public static void Apply(string? theme)
    {
        Preference = string.IsNullOrWhiteSpace(theme) ? "System" : theme;

        IsLight = Preference == "Light"
                  || (Preference != "Dark" && Application.Current.RequestedTheme == ApplicationTheme.Light);

        lock (Gate)
        {
            foreach (var reference in TitleBars.ToList())
            {
                if (reference.TryGetTarget(out var appWindow))
                    PaintTitleBar(appWindow);
            }

            foreach (var reference in Roots.ToList())
            {
                if (!reference.TryGetTarget(out var root)) continue;
                try
                {
                    root.RequestedTheme = Resolve(Preference);
                    IsLight = root.ActualTheme == ElementTheme.Light;
                }
                catch { /* window already closed */ }
            }
        }

        try { PaletteChanged?.Invoke(); }
        catch { /* a view failing to repaint must not abort the theme switch */ }
    }

    /// <summary>Brush from the palette of the theme that is currently in use.</summary>
    public static Brush Brush(string key)
    {
        var resources = Application.Current?.Resources;
        if (resources != null)
        {
            try
            {
                // The palette lives in Resources/Styles.xaml, which is merged into
                // Application.Resources — so walk the merged dictionaries instead of
                // looking only at the top level.
                var dictionaries = Enumerate(resources).ToList();
                var themeKey = IsLight ? "Light" : "Default";

                foreach (var dictionary in dictionaries)
                {
                    var themes = dictionary.ThemeDictionaries;
                    if (themes != null && themes.ContainsKey(themeKey) && themes[themeKey] is ResourceDictionary themed)
                    {
                        if (themed.ContainsKey(key) && themed[key] is Brush themedBrush)
                            return themedBrush;
                    }
                }

                foreach (var dictionary in dictionaries)
                {
                    if (dictionary.ContainsKey(key) && dictionary[key] is Brush directBrush)
                        return directBrush;
                }
            }
            catch { /* fall through to the neutral fallback */ }
        }

        return new SolidColorBrush(Colors.Gray);
    }

    private static IEnumerable<ResourceDictionary> Enumerate(ResourceDictionary root)
    {
        yield return root;

        foreach (var merged in root.MergedDictionaries)
        {
            foreach (var nested in Enumerate(merged))
                yield return nested;
        }
    }
}
