using NineLivesAudio.Services;

namespace NineLivesAudio.Helpers;

/// <summary>
/// Every file the app writes at runtime — settings, token fallback, SQLite database, logs —
/// lives in one folder so an install can be copied, backed up or deleted as a single unit.
/// <para>
/// Resolution order:
/// </para>
/// <list type="number">
/// <item><c>%NINELIVES_DATA_DIR%</c> — explicit override (portable stick, network share, …).</item>
/// <item><c>&lt;exe folder&gt;\data</c> — the default: "my own folder".</item>
/// <item><c>%LOCALAPPDATA%\NineLivesAudio</c> — only when the install folder is not writable
/// (e.g. installed under Program Files by an admin).</item>
/// </list>
/// </summary>
public static class AppPaths
{
    public const string DataFolderName = "data";

    private static readonly Lazy<string> RootHolder =
        new(Resolve, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Folder that holds all runtime data.</summary>
    public static string DataRoot => RootHolder.Value;

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string TokenFile => Path.Combine(DataRoot, ".auth_token_dpapi");
    public static string MigratedFlag => Path.Combine(DataRoot, "migrated.flag");
    public static string DatabaseFile => Path.Combine(DataRoot, "audiobookshelf.db");
    public static string LogsDirectory => Path.Combine(DataRoot, "Logs");

    /// <summary>Where earlier versions kept everything, kept around for migration and as a fallback.</summary>
    public static string LegacyRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NineLivesAudio");

    /// <summary>True when the data really sits in the app's own folder (the normal case).</summary>
    public static bool IsPortable =>
        string.Equals(DataRoot, Path.Combine(AppContext.BaseDirectory, DataFolderName), StringComparison.OrdinalIgnoreCase);

    public static string Describe()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NINELIVES_DATA_DIR")))
            return $"{DataRoot} (NINELIVES_DATA_DIR)";
        return IsPortable ? $"{DataRoot} (next to the app)" : $"{DataRoot} (install folder not writable)";
    }

    private static string Resolve()
    {
        var overrideDir = Environment.GetEnvironmentVariable("NINELIVES_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(overrideDir.Trim());
                if (Prepare(expanded)) return expanded;
            }
            catch { /* fall through to the app folder */ }
        }

        var portable = Path.Combine(AppContext.BaseDirectory, DataFolderName);
        if (Prepare(portable)) return portable;

        // Read-only install location (Program Files): keep working from the old per-user path.
        try { Directory.CreateDirectory(LegacyRoot); } catch { /* best effort */ }
        return LegacyRoot;
    }

    /// <summary>Creates the folder and proves it is writable, so we never pick a read-only location.</summary>
    private static bool Prepare(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, ".write-probe");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// First run of a build that keeps its data next to the exe: bring over the settings,
    /// the database and the logs from the old <c>%LOCALAPPDATA%\NineLivesAudio</c> folder so
    /// the library, saved progress and server login survive the move. Existing files are
    /// never overwritten, so it is safe to call on every start.
    /// </summary>
    public static void MigrateLegacyDataIfNeeded()
    {
        try
        {
            if (string.Equals(DataRoot, LegacyRoot, StringComparison.OrdinalIgnoreCase)) return;

            // Possible previous homes, newest layout first:
            //  · install folder\data   (1.0.2 installed version, installer default location)
            //  · %LOCALAPPDATA%\NineLivesAudio (1.0.1 and older)
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Path.Combine(programs, "Programs", "Nine Lives Audio", "data"),
                Path.Combine(programs, "Programs", "NineLivesAudio", "data"),
                LegacyRoot
            };

            var source = candidates.FirstOrDefault(dir =>
                !string.Equals(Path.GetFullPath(dir), Path.GetFullPath(DataRoot), StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(dir, "settings.json")));

            if (source == null) return;

            var copied = new List<string>();
            foreach (var name in new[]
                     {
                         "settings.json", ".auth_token_dpapi", "migrated.flag",
                         "audiobookshelf.db", "audiobookshelf.db-wal", "audiobookshelf.db-shm"
                     })
            {
                var from = Path.Combine(source!, name);
                var target = Path.Combine(DataRoot, name);
                if (!File.Exists(from) || File.Exists(target)) continue;
                try
                {
                    File.Copy(from, target);
                    copied.Add(name);
                }
                catch { /* skip this one, keep the rest */ }
            }

            // Logs are only history, but having them in the same folder is the point of the move.
            try
            {
                var oldLogs = Path.Combine(source, "Logs");
                if (Directory.Exists(oldLogs))
                {
                    var newLogs = LogsDirectory;
                    Directory.CreateDirectory(newLogs);
                    foreach (var log in Directory.GetFiles(oldLogs, "app_*.log"))
                    {
                        var target = Path.Combine(newLogs, Path.GetFileName(log));
                        if (!File.Exists(target)) File.Copy(log, target);
                    }
                }
            }
            catch { /* optional */ }

            if (copied.Count > 0)
            {
                LoggingService.LogStatic(LogLevel.Info,
                    $"[Paths] Data folder: {DataRoot} — imported {string.Join(", ", copied)} from {source}");
            }
        }
        catch { /* migration is best effort; services recreate what is missing */ }
    }
}
