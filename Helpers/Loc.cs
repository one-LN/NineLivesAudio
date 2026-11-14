using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NineLivesAudio.Services;

namespace NineLivesAudio.Helpers;

/// <summary>
/// XAML attached properties for localizing static text without code-behind.
/// <para>
/// Usage: <c>&lt;TextBlock loc:Loc.Text="LibraryTitle"/&gt;</c>,
/// <c>&lt;Button loc:Loc.Content="Retry"/&gt;</c>,
/// <c>&lt;ToggleSwitch loc:Loc.Header="HideFinished"/&gt;</c>,
/// <c>&lt;TextBox loc:Loc.Placeholder="EnterUsername"/&gt;</c>.
/// </para>
/// <para>
/// Values are applied immediately when the XAML is parsed and re-applied on
/// <see cref="ILocalizationService.LanguageChanged"/>, so switching language
/// updates the UI in place with no restart and no page re-navigation.
/// </para>
/// </summary>
public sealed class Loc : DependencyObject
{
    private enum Kind { Text, Content, Header, Placeholder }

    private sealed class Entry
    {
        public required WeakReference<DependencyObject> Target { get; init; }
        public required string Key { get; init; }
        public required Kind Kind { get; init; }
    }

    private static readonly List<Entry> Entries = new();
    private static readonly object EntriesLock = new();

    private static ILocalizationService? _service;
    private static DispatcherQueue? _dispatcher;

    /// <summary>Wires the service that supplies strings. Call once from <c>App.OnLaunched</c>.</summary>
    public static void Initialize(ILocalizationService service, DispatcherQueue dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;
        service.LanguageChanged += ReapplyAll;
    }

    /// <summary>Re-applies every registered key; runs on the UI thread.</summary>
    public static void ReapplyAll()
    {
        if (_dispatcher != null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(ReapplyAll);
            return;
        }

        List<Entry> snapshot;
        lock (EntriesLock)
        {
            // Drop entries whose element has been collected (page navigations).
            Entries.RemoveAll(e => !e.Target.TryGetTarget(out _));
            snapshot = new List<Entry>(Entries);
        }

        foreach (var entry in snapshot)
        {
            if (!entry.Target.TryGetTarget(out var target)) continue;
            Apply(target, entry.Key, entry.Kind);
        }
    }

    // ── Attached property definitions ────────────────────────────────────

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Loc),
        new PropertyMetadata(null, (d, e) => OnAttached(d, e.NewValue as string, Kind.Text)));

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
    public static string GetText(DependencyObject element) => element.GetValue(TextProperty) as string ?? string.Empty;

    public static readonly DependencyProperty ContentProperty = DependencyProperty.RegisterAttached(
        "Content", typeof(string), typeof(Loc),
        new PropertyMetadata(null, (d, e) => OnAttached(d, e.NewValue as string, Kind.Content)));

    public static void SetContent(DependencyObject element, string value) => element.SetValue(ContentProperty, value);
    public static string GetContent(DependencyObject element) => element.GetValue(ContentProperty) as string ?? string.Empty;

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.RegisterAttached(
        "Header", typeof(string), typeof(Loc),
        new PropertyMetadata(null, (d, e) => OnAttached(d, e.NewValue as string, Kind.Header)));

    public static void SetHeader(DependencyObject element, string value) => element.SetValue(HeaderProperty, value);
    public static string GetHeader(DependencyObject element) => element.GetValue(HeaderProperty) as string ?? string.Empty;

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Loc),
        new PropertyMetadata(null, (d, e) => OnAttached(d, e.NewValue as string, Kind.Placeholder)));

    public static void SetPlaceholder(DependencyObject element, string value) => element.SetValue(PlaceholderProperty, value);
    public static string GetPlaceholder(DependencyObject element) => element.GetValue(PlaceholderProperty) as string ?? string.Empty;

    // ── Plumbing ─────────────────────────────────────────────────────────

    private static void OnAttached(DependencyObject target, string? key, Kind kind)
    {
        if (target == null || string.IsNullOrEmpty(key)) return;

        // Apply immediately for correct first paint...
        Apply(target, key, kind);

        // ...and once more when the element is loaded. A literal attribute such as
        // Text="Library" in the same tag would otherwise win whenever the XAML
        // parser applies it after this attached property.
        if (target is FrameworkElement element)
        {
            element.Loaded += (_, _) => Apply(target, key, kind);
        }

        lock (EntriesLock)
        {
            Entries.RemoveAll(e => !e.Target.TryGetTarget(out _));
            Entries.Add(new Entry
            {
                Target = new WeakReference<DependencyObject>(target),
                Key = key,
                Kind = kind
            });
        }
    }

    private static void Apply(DependencyObject target, string key, Kind kind)
    {
        var text = _service?.GetString(key) ?? key;

        switch (kind)
        {
            case Kind.Text when target is TextBlock textBlock:
                textBlock.Text = text;
                break;

            case Kind.Content when target is ContentControl contentControl:
                contentControl.Content = text;
                break;

            case Kind.Header when target is ToggleSwitch toggle:
                toggle.Header = text;
                break;

            case Kind.Header when target is ComboBox combo:
                combo.Header = text;
                break;

            case Kind.Header when target is TextBox textBox:
                textBox.Header = text;
                break;

            case Kind.Header when target is PasswordBox passwordBox:
                passwordBox.Header = text;
                break;

            case Kind.Placeholder when target is TextBox placeholderBox:
                placeholderBox.PlaceholderText = text;
                break;

            case Kind.Placeholder when target is PasswordBox placeholderPassword:
                placeholderPassword.PlaceholderText = text;
                break;

            case Kind.Placeholder when target is ComboBox placeholderCombo:
                placeholderCombo.PlaceholderText = text;
                break;

            case Kind.Placeholder when target is AutoSuggestBox placeholderSuggest:
                placeholderSuggest.PlaceholderText = text;
                break;

            case Kind.Placeholder when target is RichEditBox placeholderRich:
                placeholderRich.PlaceholderText = text;
                break;
        }
    }
}
