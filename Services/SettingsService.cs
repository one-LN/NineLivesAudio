using NineLivesAudio.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Security.Credentials;

namespace NineLivesAudio.Services;

public class SettingsService : ISettingsService
{
    private const string VaultResource = "NineLivesAudio.AuthToken";
    private const string VaultUser = "default";
    private const string SettingsFile = "settings.json";
    private const string MigratedFlag = "migrated.flag";
    private const string TokenFile = ".auth_token_dpapi";

    private AppSettings? _settings;
    private readonly string _appFolder;
    private readonly string _settingsPath;
    private readonly string _tokenPath;
    private readonly string _migratedPath;

    /// <summary>Metadata for Settings Doctor UI.</summary>
    public string SettingsFilePath => _settingsPath;
    public string SettingsSource { get; private set; } = "new";
    public string TokenSource { get; private set; } = "none";
    public DateTime? LastLoadedAt { get; private set; }

    public AppSettings Settings => _settings ??= new AppSettings();

    public SettingsService()
    {
        _appFolder = NineLivesAudio.Helpers.AppPaths.DataRoot;
        Directory.CreateDirectory(_appFolder);

        _settingsPath = NineLivesAudio.Helpers.AppPaths.SettingsFile;
        _tokenPath = NineLivesAudio.Helpers.AppPaths.TokenFile;
        _migratedPath = NineLivesAudio.Helpers.AppPaths.MigratedFlag;
    }

    public async Task LoadSettingsAsync()
    {
        // 1. Try primary source
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);

                if (loaded != null && IsValid(loaded))
                {
                    _settings = loaded;
                    SettingsSource = "file";
                    LastLoadedAt = DateTime.Now;
                    NormalizeSettings();
                    return;
                }
                else
                {
                    // Corrupt or empty JSON — back up and reset
                    var backupPath = _settingsPath + ".bak";
                    try { File.Copy(_settingsPath, backupPath, overwrite: true); } catch { /* best-effort: ignore */ }
                    SettingsSource = "reset_corrupt";
                }
            }
            catch (JsonException)
            {
                // Corrupt JSON — backup and reset
                var backupPath = _settingsPath + ".bak";
                try { File.Copy(_settingsPath, backupPath, overwrite: true); } catch { /* best-effort: ignore */ }
                SettingsSource = "reset_corrupt";
            }
            catch (Exception)
            {
                SettingsSource = "reset_error";
            }
        }

        // 2. One-time migration from legacy (ApplicationData)
        if (!File.Exists(_migratedPath))
        {
            try
            {
                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var file = await localFolder.TryGetItemAsync(SettingsFile) as Windows.Storage.StorageFile;
                if (file != null)
                {
                    var json = await Windows.Storage.FileIO.ReadTextAsync(file);
                    var migrated = JsonSerializer.Deserialize<AppSettings>(json);
                    if (migrated != null && !string.IsNullOrWhiteSpace(migrated.ServerUrl))
                    {
                        _settings = migrated;
                        SettingsSource = "migrated";
                        await SaveSettingsAsync();
                    }
                }
            }
            catch { /* ApplicationData not available for unpackaged */ }

            // Write migration flag so we never try again
            try { await File.WriteAllTextAsync(_migratedPath, DateTime.Now.ToString("o")); } catch { /* best-effort: ignore */ }
        }

        // 3. Defaults if nothing loaded
        if (_settings == null)
        {
            _settings = new AppSettings();
            SettingsSource = SettingsSource == "reset_corrupt" ? "reset_corrupt" : "new";
            await SaveSettingsAsync();
        }

        NormalizeSettings();
        LastLoadedAt = DateTime.Now;
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            NormalizeSettings();
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex.Message}");
        }
    }

    public Task<string?> GetAuthTokenAsync()
    {
        // 1. PasswordVault (primary)
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(VaultResource, VaultUser);
            cred.RetrievePassword();
            if (!string.IsNullOrEmpty(cred.Password))
            {
                TokenSource = "vault";
                return Task.FromResult<string?>(cred.Password);
            }
        }
        catch { /* Not found or vault unavailable */ }

        // 2. DPAPI-encrypted file (fallback)
        try
        {
            if (File.Exists(_tokenPath))
            {
                var encrypted = File.ReadAllBytes(_tokenPath);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var token = Encoding.UTF8.GetString(decrypted);
                if (!string.IsNullOrEmpty(token))
                {
                    TokenSource = "dpapi_file";
                    return Task.FromResult<string?>(token);
                }
            }
        }
        catch { /* DPAPI failed, file corrupt, etc. */ }

        // 3. Legacy plain text fallback (read-only, for migration)
        try
        {
            var legacyPath = Path.Combine(_appFolder, ".auth_token");
            if (File.Exists(legacyPath))
            {
                var token = File.ReadAllText(legacyPath).Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    TokenSource = "legacy_file";
                    // Migrate to secure storage
                    _ = SaveAuthTokenAsync(token);
                    try { File.Delete(legacyPath); } catch { /* best-effort: ignore */ }
                    return Task.FromResult<string?>(token);
                }
            }
        }
        catch { /* best-effort: ignore */ }

        TokenSource = "none";
        return Task.FromResult<string?>(null);
    }

    public Task SaveAuthTokenAsync(string token)
    {
        // 1. PasswordVault
        try
        {
            var vault = new PasswordVault();
            try { vault.Remove(vault.Retrieve(VaultResource, VaultUser)); } catch { /* best-effort: ignore */ }
            vault.Add(new PasswordCredential(VaultResource, VaultUser, token));
        }
        catch { /* Vault unavailable */ }

        // 2. DPAPI-encrypted file (always, as fallback)
        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(token);
            var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_tokenPath, encrypted);
        }
        catch { /* DPAPI failed */ }

        return Task.CompletedTask;
    }

    public Task ClearAuthTokenAsync()
    {
        // Clear vault
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(VaultResource, VaultUser);
            vault.Remove(cred);
        }
        catch { /* best-effort: ignore */ }

        // Clear DPAPI file
        try { if (File.Exists(_tokenPath)) File.Delete(_tokenPath); } catch { /* best-effort: ignore */ }

        // Clear legacy file
        try
        {
            var legacyPath = Path.Combine(_appFolder, ".auth_token");
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }
        catch { /* best-effort: ignore */ }

        TokenSource = "none";
        return Task.CompletedTask;
    }

    private void NormalizeSettings()
    {
        if (_settings == null) return;

        // Trim trailing slashes from ServerUrl
        if (!string.IsNullOrEmpty(_settings.ServerUrl))
            _settings.ServerUrl = _settings.ServerUrl.TrimEnd('/');
    }

    private static bool IsValid(AppSettings s)
    {
        // A deserialized AppSettings with all defaults is valid (user may not have connected yet)
        return s != null;
    }
}
