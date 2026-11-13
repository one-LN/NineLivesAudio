using System.Globalization;

namespace NineLivesAudio.Helpers;

/// <summary>
/// Ambient UI language flag shared by helpers and models that have no DI access.
/// <see cref="NineLivesAudio.Services.LocalizationService"/> keeps this in sync with the user's
/// preference, so texts produced by <see cref="TimeFormatHelper"/> and models
/// follow the language chosen in Settings rather than the OS language.
/// </summary>
public static class AppLanguage
{
    private static bool? _forcedIsChinese;

    /// <summary>True when the active UI language is Simplified Chinese.</summary>
    public static bool IsChinese
    {
        get
        {
            if (_forcedIsChinese.HasValue) return _forcedIsChinese.Value;
            return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Called by the localization service whenever the language changes.</summary>
    public static void SetChinese(bool isChinese) => _forcedIsChinese = isChinese;

    /// <summary>Language-neutral short date format (e.g. "Mar 4", "3月4日").</summary>
    public static string FormatShortDate(DateTime value) =>
        IsChinese ? value.ToString("M月d日") : value.ToString("MMM d", CultureInfo.InvariantCulture);
}
