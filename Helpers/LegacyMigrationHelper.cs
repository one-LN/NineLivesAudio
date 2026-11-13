using Windows.Security.Credentials;

namespace NineLivesAudio.Helpers;

/// <summary>
/// One-time migration from legacy "AudioBookshelfApp" folder and vault names
/// to the rebranded "NineLivesAudio" names. Safe to call on every startup —
/// it no-ops when there is nothing to migrate.
/// </summary>
public static class LegacyMigrationHelper
{
    private const string LegacyFolderName = "AudioBookshelfApp";
    private const string NewFolderName = "NineLivesAudio";
    private const string LegacyVaultResource = "AudioBookshelfApp.AuthToken";
    private const string NewVaultResource = "NineLivesAudio.AuthToken";
    private const string VaultUser = "default";

    public static void MigrateIfNeeded()
    {
        MigrateAppDataFolder();
        MigrateTempFolder();
        MigrateVaultCredential();
    }

    private static void MigrateAppDataFolder()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var oldPath = Path.Combine(localAppData, LegacyFolderName);
            var newPath = Path.Combine(localAppData, NewFolderName);

            if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
            {
                Directory.Move(oldPath, newPath);
            }
        }
        catch
        {
            // Best effort — services will create the new folder if rename fails
        }
    }

    private static void MigrateTempFolder()
    {
        try
        {
            var oldPath = Path.Combine(Path.GetTempPath(), LegacyFolderName);
            var newPath = Path.Combine(Path.GetTempPath(), NewFolderName);

            if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
            {
                Directory.Move(oldPath, newPath);
            }
        }
        catch
        {
            // Temp files are transient, not critical
        }
    }

    private static void MigrateVaultCredential()
    {
        try
        {
            var vault = new PasswordVault();
            var oldCred = vault.Retrieve(LegacyVaultResource, VaultUser);
            oldCred.RetrievePassword();

            if (!string.IsNullOrEmpty(oldCred.Password))
            {
                // Remove any existing new-key credential before adding
                try { vault.Remove(vault.Retrieve(NewVaultResource, VaultUser)); } catch { /* best-effort: ignore */ }
                vault.Add(new PasswordCredential(NewVaultResource, VaultUser, oldCred.Password));
                vault.Remove(oldCred);
            }
        }
        catch
        {
            // Old credential doesn't exist or vault unavailable — nothing to migrate
        }
    }
}
