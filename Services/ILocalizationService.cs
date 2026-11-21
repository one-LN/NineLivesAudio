namespace NineLivesAudio.Services;

/// <summary>
/// Provides localized UI strings for English and Simplified Chinese.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Current language code: "en-US" or "zh-CN".</summary>
    string CurrentLanguage { get; }

    /// <summary>True when the current language is Simplified Chinese.</summary>
    bool IsChinese { get; }

    /// <summary>Gets a localized string by key. Falls back to English, then the key itself.</summary>
    string GetString(string key);

    /// <summary>Gets a localized string and formats it with the given arguments.</summary>
    string Format(string key, params object?[] args);

    /// <summary>Raised when the language changes so views can refresh.</summary>
    event Action? LanguageChanged;

    /// <summary>Applies a language ("en-US", "zh-CN", or "system") and persists it.</summary>
    Task SetLanguageAsync(string language);

    /// <summary>
    /// Re-reads the persisted preference. Call after settings are loaded at startup
    /// so a saved language takes effect before the UI is built.
    /// </summary>
    void RefreshFromSettings();
}
