using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadSettingsAsync();
    Task SaveSettingsAsync();
    Task<string?> GetAuthTokenAsync();
    Task SaveAuthTokenAsync(string token);
    Task ClearAuthTokenAsync();

    /// <summary>
    /// Records a server/account pair (deduplicated) so the settings page can offer it later.
    /// Tokens are stored per server, so switching back restores the previous session.
    /// </summary>
    void RememberServer(string serverUrl, string username, string? name = null);
    void RemoveServer(string serverUrl, string username);

    // Settings Doctor metadata
    string SettingsFilePath { get; }
    string SettingsSource { get; }
    string TokenSource { get; }
    DateTime? LastLoadedAt { get; }
}
