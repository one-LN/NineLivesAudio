using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NineLivesAudio.Launcher;

/// <summary>
/// Starts Nine Lives Audio from a single file.
/// <para>
/// The real publish output is embedded as <c>payload.zip</c>. On the first run of a new
/// payload it is unpacked into <c>app\</c> next to this executable; afterwards the loader
/// starts the app directly, so only the first launch is slow.
/// </para>
/// <para>
/// Data (settings, database, logs) lives in <c>data\</c> next to this executable — the
/// launcher exports <c>NINELIVES_DATA_DIR</c>, which the app honours — so the whole
/// installation is one exe plus one folder that can be copied or backed up as a unit.
/// </para>
/// </summary>
internal static class Program
{
    private const string PayloadResource = "payload.zip";
    private const string AppFolder = "app";
    private const string DataFolder = "data";
    private const string StampFile = ".payload-stamp";
    private const string AppExe = "NineLivesAudio.exe";

    [STAThread]
    private static int Main()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(exeDir)) exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            if (exeDir.EndsWith(Path.DirectorySeparatorChar)) exeDir = exeDir.TrimEnd(Path.DirectorySeparatorChar);

            var appDir = Path.Combine(exeDir, AppFolder);
            var dataDir = Path.Combine(exeDir, DataFolder);
            Directory.CreateDirectory(dataDir);

            // The app resolves its data folder from this variable before it reads anything.
            Environment.SetEnvironmentVariable("NINELIVES_DATA_DIR", dataDir);

            var appExePath = Path.Combine(appDir, AppExe);
            var stamp = CurrentStamp();

            // Two quick double-clicks must not unpack into the same folder at the same time.
            using var mutex = new Mutex(false, @"Local\NineLivesAudio.SingleFileLauncher");
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(30));
                if (!IsUpToDate(appDir, appExePath, stamp))
                {
                    Unpack(appDir, stamp);
                }
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }

            var startInfo = new ProcessStartInfo(appExePath)
            {
                UseShellExecute = false,
                WorkingDirectory = appDir
            };
            Process.Start(startInfo);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero,
                "Nine Lives Audio could not start.\r\n\r\n" + ex.Message +
                "\r\n\r\nIf the app is running, close it and try again.",
                "Nine Lives Audio", 0x00000010 /* MB_ICONERROR */);
            return 1;
        }
    }

    private static bool IsUpToDate(string appDir, string appExePath, string stamp)
    {
        try
        {
            var stampPath = Path.Combine(appDir, StampFile);
            return File.Exists(appExePath)
                   && File.Exists(stampPath)
                   && string.Equals(File.ReadAllText(stampPath).Trim(), stamp, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Identifies the embedded payload without hashing 80 MB on every start: the payload is
    /// part of this executable, so its version and size change with every build.
    /// </summary>
    private static string CurrentStamp()
    {
        try
        {
            var path = Environment.ProcessPath ?? string.Empty;
            var info = new FileInfo(path);
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "0";
            return $"{version}-{info.Length}";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void Unpack(string appDir, string stamp)
    {
        // A different build: the previous output has to go, otherwise stale native DLLs stay
        // mixed with new ones. Fails loudly (and helpfully) when the old app is still running.
        if (Directory.Exists(appDir))
        {
            try
            {
                Directory.Delete(appDir, recursive: true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The previous version could not be replaced (" + ex.Message + ").", ex);
            }
        }

        Directory.CreateDirectory(appDir);

        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("This build has no embedded payload (payload.zip).");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);

        long written = 0;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(appDir, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(appDir), StringComparison.OrdinalIgnoreCase))
                continue; // zip-slip guard, cannot happen with our own payload

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            written += entry.Length;
        }

        if (written <= 0)
            throw new InvalidOperationException("The embedded payload is empty.");

        File.WriteAllText(Path.Combine(appDir, StampFile), stamp);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
