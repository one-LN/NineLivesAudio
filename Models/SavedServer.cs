namespace NineLivesAudio.Models;

/// <summary>
/// One remembered server/account pair. Only the address and the display name are stored
/// here — the token itself lives in its own per-server file (see SettingsService).
/// </summary>
public class SavedServer
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime LastUsedAt { get; set; }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name.Trim();

            var host = Url;
            try { host = new Uri(Url).Host; } catch { /* keep raw text */ }

            return string.IsNullOrWhiteSpace(Username) ? host : $"{host} · {Username}";
        }
    }

    public bool Matches(string url, string username)
        => string.Equals(Normalize(Url), Normalize(url), StringComparison.OrdinalIgnoreCase)
           && string.Equals(Username ?? string.Empty, username ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? url)
        => string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');
}
