namespace NineLivesAudio.Models;

public class AppSettings
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DownloadPath { get; set; } = string.Empty;
    public bool AutoDownloadCovers { get; set; } = true;
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool AutoSyncProgress { get; set; } = true;

    /// <summary>Seconds skipped automatically when a book starts playing (0 = disabled).</summary>
    public int SkipIntroSeconds { get; set; } = 0;

    /// <summary>
    /// When fewer than this many seconds remain, playback continues with the next
    /// chapter (0 = disabled). On the last chapter the book is marked finished.
    /// </summary>
    public int SkipOutroSeconds { get; set; } = 0;
    public int SyncIntervalMinutes { get; set; } = 5;
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public double Volume { get; set; } = 0.8;
    public string Theme { get; set; } = "System"; // System, Light, Dark

    /// <summary>UI language: "system" | "en-US" | "zh-CN".</summary>
    public string Language { get; set; } = "system";

    // Security
    public bool AllowSelfSignedCertificates { get; set; } = false;

    // Diagnostics
    public bool DiagnosticsMode { get; set; } = false;

    // Server profiles for reconnection
    public List<ServerProfile> ServerProfiles { get; set; } = new();
}
