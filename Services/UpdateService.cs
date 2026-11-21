using System.IO.Compression;
using System.Text.Json;

namespace NineLivesAudio.Services;

/// <summary>Result of asking GitHub for the newest published release.</summary>
public sealed record UpdateCheckResult(
    bool Succeeded,
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? Error,
    string? PortableZipUrl = null,
    string? SetupUrl = null);

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdateAsync();

    /// <summary>True when this copy was laid down by the installer (Lives under %LocalAppData%\Programs).</summary>
    bool IsInstalledCopy { get; }

    /// <summary>Downloads a release asset, reporting 0–1 progress.</summary>
    Task<string> DownloadAsync(string url, IProgress<double>? progress, CancellationToken ct = default);

    /// <summary>
    /// Portable copy: unpacks the zip next to the exe and starts a tiny helper that swaps the
    /// files once this process has exited. Returns the helper's path.
    /// </summary>
    Task<string> StagePortableUpdateAsync(string zipPath);

    /// <summary>Installed copy: runs the setup silently; Inno closes and restarts the app.</summary>
    void RunInstaller(string setupPath);
}

/// <summary>
/// Talks to the fork that ships these builds. Updating replaces the executable only after the
/// user asks for it — nothing happens in the background.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/one-LN/NineLivesAudio/releases/latest";

    private readonly ILoggingService _logger;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public UpdateService(ILoggingService logger) => _logger = logger;

    public bool IsInstalledCopy
    {
        get
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;

                // The installer puts the app under %LocalAppData%\Programs («Nine Lives Audio»);
                // matching the parent folder rather than the exact name keeps this working if
                // that name ever changes. A portable copy lives wherever the zip was unpacked.
                var installedRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs");

                return exe.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        var current = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);
        var currentText = $"{current.Major}.{current.Minor}.{Math.Max(current.Build, 0)}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);

            // GitHub's API rejects requests without a user agent.
            request.Headers.UserAgent.ParseAdd($"NineLivesAudio/{currentText}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            _logger.Log($"[Update] Checking for updates (current {currentText})");

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var root = json.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;

            string? portableUrl = null;
            string? setupUrl = null;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    var download = asset.TryGetProperty("browser_download_url", out var downloadElement)
                        ? downloadElement.GetString()
                        : null;

                    if (name == null || download == null) continue;

                    if (name.EndsWith("-portable.zip", StringComparison.OrdinalIgnoreCase))
                        portableUrl = download;
                    else if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
                        setupUrl = download;
                }
            }

            var latest = ParseVersion(tag);
            if (latest == null)
            {
                _logger.LogWarning($"[Update] Release tag '{tag}' is not a version — cannot compare");
                return new UpdateCheckResult(false, false, currentText, tag, url, "Unrecognised release tag");
            }

            var newer = latest > current;
            var latestText = $"{latest.Major}.{latest.Minor}.{Math.Max(latest.Build, 0)}";

            _logger.Log(newer
                ? $"[Update] Newer release available: {tag} (current {currentText})"
                : $"[Update] Already on the latest version ({currentText})");

            return new UpdateCheckResult(true, newer, currentText, tag, url, null, portableUrl, setupUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Update] Check failed: {ex.Message}");
            return new UpdateCheckResult(false, false, currentText, null, null, ex.Message);
        }
    }

    public async Task<string> DownloadAsync(string url, IProgress<double>? progress, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(fileName)) fileName = "NineLivesAudio-update.zip";

        var destination = Path.Combine(Path.GetTempPath(), fileName);

        // A truncated download must never be mistaken for a good one.
        try { File.Delete(destination); } catch { /* leftovers are fine */ }

        _logger.Log($"[Update] Downloading {url}");

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;

        await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var output = File.Create(destination))
        {
            var buffer = new byte[81920];
            long written = 0;
            int read;

            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                written += read;

                if (total > 0) progress?.Report((double)written / total);
            }
        }

        _logger.Log($"[Update] Downloaded {new FileInfo(destination).Length / 1024 / 1024} MB to {destination}");
        return destination;
    }

    public Task<string> StagePortableUpdateAsync(string zipPath)
    {
        var exePath = Environment.ProcessPath;
        var exeDir = exePath != null ? Path.GetDirectoryName(exePath) : null;

        if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir))
            throw new InvalidOperationException("Cannot determine the folder this build runs from.");

        var staging = Path.Combine(Path.GetTempPath(), "NineLivesAudio-update");

        try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { /* in use */ }
        Directory.CreateDirectory(staging);

        ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
        _logger.Log($"[Update] Unpacked the new build into {staging}");

        // The running exe cannot be overwritten, so a helper waits for this process to exit and
        // then mirrors the new files over the old folder. `data` is excluded: that is the user's
        // library, cache and logs, not part of a build.
        var helper = Path.Combine(Path.GetTempPath(), "NineLivesAudio-update.cmd");
        var script = $"""
            @echo off
            setlocal enabledelayedexpansion
            set tries=0
            :wait
            tasklist /FI "IMAGENAME eq NineLivesAudio.exe" | find /I "NineLivesAudio.exe" >nul
            if not errorlevel 1 (
              set /a tries+=1
              if !tries! GEQ 90 goto giveup
              timeout /t 1 /nobreak >nul
              goto wait
            )
            robocopy "{staging}" "{exeDir}" /E /IS /IT /XD "{Path.Combine(exeDir, "data")}" /XF "*.log" /NFL /NDL /NJH /NJS /NP >nul
            start "" "{exePath}"
            rmdir /s /q "{staging}"
            del "%~f0"
            exit /b 0
            :giveup
            del "%~f0"
            """;

        File.WriteAllText(helper, script);
        _logger.Log($"[Update] Wrote the update helper: {helper}");

        return Task.FromResult(helper);
    }

    public void RunInstaller(string setupPath)
    {
        // PrivilegesRequired=lowest, so no UAC prompt; Inno closes the running app itself and
        // starts it again afterwards.
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true
        };

        _logger.Log($"[Update] Running the installer: {setupPath}");
        System.Diagnostics.Process.Start(startInfo);
    }

    /// <summary>"v1.0.24" / "1.0.24" / "1.0.24-beta" → Version.</summary>
    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        var dash = text.IndexOf('-');
        if (dash >= 0) text = text[..dash];

        return Version.TryParse(text, out var version) ? version : null;
    }
}
