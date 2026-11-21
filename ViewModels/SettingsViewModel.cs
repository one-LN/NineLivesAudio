using System.Collections.ObjectModel;
using NineLivesAudio.Data;
using NineLivesAudio.Helpers;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NineLivesAudio.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILocalDatabase _database;
    private readonly ILoggingService _logger;
    private readonly ISyncService _syncService;
    private readonly ILocalizationService _localization;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IUpdateService _updateService;
    private readonly IStreamCacheService _streamCache;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditServerFields))]
    [NotifyPropertyChangedFor(nameof(ShowConnectedActions))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditServerFields))]
    [NotifyPropertyChangedFor(nameof(ShowConnectedActions))]
    private bool _isAddingServer;

    public bool CanEditServerFields => !IsConnected || IsAddingServer;
    public bool ShowConnectedActions => IsConnected && !IsAddingServer;

    private string _previousServerUrl = string.Empty;
    private string _previousUsername = string.Empty;
    private string _previousServerName = string.Empty;

    [ObservableProperty]
    private string _serverName = string.Empty;

    [ObservableProperty]
    private string _serverNameStatus = string.Empty;

    public bool HasSelectedServer => SelectedServer != null;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string _connectionStatus = string.Empty; // set from localization in ctor

    [ObservableProperty]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private bool _autoDownloadCovers;

    [ObservableProperty]
    private double _playbackSpeed;

    // Starts true: x:Bind pushes control defaults (Slider 0, ComboBox -1) before
    // InitializeAsync runs, and those must never be persisted as the user's choice.
    private bool _isLoadingSettings = true;

    [ObservableProperty]
    private double _skipIntroSeconds;

    [ObservableProperty]
    private double _skipOutroSeconds;

    /// <summary>Progress display: chapter slider vs. whole-book slider (same flag the player uses).</summary>
    [ObservableProperty]
    private bool _playerChapterMode;

    [ObservableProperty]
    private bool _autoSyncProgress;

    [ObservableProperty]
    private int _syncIntervalMinutes;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private double _volume;

    [ObservableProperty]
    private string _theme = "System";

    [ObservableProperty]
    private string _language = "system";

    // Security
    [ObservableProperty]
    private bool _allowSelfSignedCertificates;

    // Diagnostics
    [ObservableProperty]
    private bool _diagnosticsMode;

    // Settings Doctor
    [ObservableProperty]
    private string _settingsFilePath = string.Empty;

    [ObservableProperty]
    private string _settingsSource = string.Empty;

    [ObservableProperty]
    private string _tokenSource = string.Empty;

    [ObservableProperty]
    private string _lastLoadedAt = string.Empty;

    [ObservableProperty]
    private string _sessionId = string.Empty;

    [ObservableProperty]
    private string _serverHost = string.Empty;

    /// <summary>Effective playback values as actually stored in settings.json.</summary>
    [ObservableProperty]
    private string _playbackFlags = string.Empty;

    // Messages
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSuccess;

    // ── Remembered servers ─────────────────────────────────────────────────
    public ObservableCollection<SavedServer> SavedServers { get; } = new();

    [ObservableProperty]
    private SavedServer? _selectedServer;

    [ObservableProperty]
    private string _serverSwitchStatus = string.Empty;

    // ── Dropdown selections (observable, so they show the saved value) ─────
    [ObservableProperty] private int _speedSelectedIndex = 2;
    [ObservableProperty] private int _syncIntervalSelectedIndex = 1;
    [ObservableProperty] private int _themeSelectedIndex;
    private bool _syncingIndexes;

    private static int IndexForSpeed(double speed) => speed switch
    {
        <= 0.5 => 0,
        <= 0.75 => 1,
        <= 1.0 => 2,
        <= 1.25 => 3,
        <= 1.5 => 4,
        <= 1.75 => 5,
        _ => 6
    };

    private static double SpeedForIndex(int index) => index switch
    {
        0 => 0.5, 1 => 0.75, 2 => 1.0, 3 => 1.25, 4 => 1.5, 5 => 1.75, 6 => 2.0, _ => 1.0
    };

    private static int IndexForInterval(int minutes) => minutes switch
    {
        <= 1 => 0,
        <= 5 => 1,
        <= 10 => 2,
        <= 15 => 3,
        _ => 4
    };

    private static int IntervalForIndex(int index) => index switch
    {
        0 => 1, 1 => 5, 2 => 10, 3 => 15, 4 => 30, _ => 5
    };

    private static int IndexForTheme(string? theme) => theme switch
    {
        "Light" => 1,
        "Dark" => 2,
        _ => 0
    };

    private static string ThemeForIndex(int index) => index switch
    {
        1 => "Light",
        2 => "Dark",
        _ => "System"
    };

    // ── Inline save feedback ────────────────────────────────────────────────
    // Every control writes its own "Saved" / error text right under itself, so nothing
    // depends on a single message at the bottom of the page any more.
    [ObservableProperty] private string _serverUrlStatus = string.Empty;
    [ObservableProperty] private string _usernameStatus = string.Empty;
    [ObservableProperty] private string _downloadPathStatus = string.Empty;
    [ObservableProperty] private string _autoDownloadCoversStatus = string.Empty;
    [ObservableProperty] private string _playbackSpeedStatus = string.Empty;
    [ObservableProperty] private string _volumeStatus = string.Empty;
    [ObservableProperty] private string _skipIntroStatus = string.Empty;
    [ObservableProperty] private string _skipOutroStatus = string.Empty;
    [ObservableProperty] private string _chapterModeStatus = string.Empty;
    [ObservableProperty] private string _autoSyncStatus = string.Empty;
    [ObservableProperty] private string _syncIntervalStatus = string.Empty;
    [ObservableProperty] private string _startMinimizedStatus = string.Empty;
    [ObservableProperty] private string _minimizeToTrayStatus = string.Empty;
    [ObservableProperty] private string _themeStatus = string.Empty;
    [ObservableProperty] private string _languageStatus = string.Empty;
    [ObservableProperty] private string _selfSignedStatus = string.Empty;
    [ObservableProperty] private string _diagnosticsStatus = string.Empty;

    public SettingsViewModel(
        ISettingsService settingsService,
        IAudioBookshelfApiService apiService,
        ILocalDatabase database,
        ILoggingService loggingService,
        ISyncService syncService,
        ILocalizationService localization,
        IAudioPlaybackService playbackService,
        IUpdateService updateService,
        IStreamCacheService streamCache)
    {
        _settingsService = settingsService;
        _apiService = apiService;
        _database = database;
        _logger = loggingService;
        _syncService = syncService;
        _localization = localization;
        _playbackService = playbackService;
        _updateService = updateService;
        _streamCache = streamCache;

        // Seed localized defaults before the page is shown.
        ConnectionStatus = _localization.GetString("NotConnectedStatus");
    }

    public async Task InitializeAsync()
    {
        await _settingsService.LoadSettingsAsync();
        await LoadSettingsToUIAsync();
        LoadDoctorInfo();

        // Check if already connected
        IsConnected = _apiService.IsAuthenticated;
        if (IsConnected)
        {
            ConnectionStatus = _localization.Format("ConnectedTo", _apiService.ServerUrl);
        }
    }

    private async Task LoadSettingsToUIAsync()
    {
        _isLoadingSettings = true;
        try
        {
            LoadSettingsToUIUnsafe();
        }
        finally
        {
            _isLoadingSettings = false;
        }

        // Cover auto-download is on by default, so a download folder has to exist from the
        // first start — store the default once instead of leaving settings.json blank.
        if (string.IsNullOrWhiteSpace(_settingsService.Settings.DownloadPath))
        {
            try
            {
                _settingsService.Settings.DownloadPath = DownloadPath;
                await _settingsService.SaveSettingsAsync();
                _logger.Log($"[Settings] Default download folder stored: {DownloadPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Settings] Could not store default download folder: {ex.Message}");
            }
        }
    }

    private void LoadSettingsToUIUnsafe()
    {
        var settings = _settingsService.Settings;

        ServerUrl = settings.ServerUrl ?? string.Empty;
        Username = settings.Username ?? string.Empty;
        // "Auto-download cover art" is on by default, so there has to be a folder even when
        // the user never picked one.
        DownloadPath = string.IsNullOrWhiteSpace(settings.DownloadPath)
            ? GetDefaultDownloadPath()
            : settings.DownloadPath;
        AutoDownloadCovers = settings.AutoDownloadCovers;
        PlaybackSpeed = settings.PlaybackSpeed;
        SkipIntroSeconds = settings.SkipIntroSeconds;
        SkipOutroSeconds = settings.SkipOutroSeconds;
        PlayerChapterMode = settings.PlayerChapterMode;
        AutoSyncProgress = settings.AutoSyncProgress;
        SyncIntervalMinutes = settings.SyncIntervalMinutes;
        StartMinimized = settings.StartMinimized;
        MinimizeToTray = settings.MinimizeToTray;
        Volume = settings.Volume;
        Theme = settings.Theme ?? "System";
        Language = settings.Language ?? "system";
        AllowSelfSignedCertificates = settings.AllowSelfSignedCertificates;
        DiagnosticsMode = settings.DiagnosticsMode;

        RefreshSavedServers();

        // Dropdowns show what is actually stored (they used to keep showing the defaults).
        _syncingIndexes = true;
        SpeedSelectedIndex = IndexForSpeed(settings.PlaybackSpeed);
        SyncIntervalSelectedIndex = IndexForInterval(settings.SyncIntervalMinutes);
        ThemeSelectedIndex = IndexForTheme(settings.Theme);
        _syncingIndexes = false;
    }

    /// <summary>Where the app keeps settings, database and logs — shown in Settings › Diagnostics.</summary>
    [ObservableProperty]
    private string _dataDirectory = string.Empty;

    [ObservableProperty]
    private bool _isSyncingNow;

    /// <summary>Real assembly version, so the About box cannot drift from the build again.</summary>
    public string AppVersion { get; } = "Version " + LoggingService.DescribeBuild();

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private bool _isInstallingUpdate;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    /// <summary>Keeps the status line hidden until the user actually asked for a check.</summary>
    public bool HasUpdateStatus => !string.IsNullOrEmpty(UpdateStatusText);

    partial void OnUpdateStatusTextChanged(string value)
        => OnPropertyChanged(nameof(HasUpdateStatus));

    private string? _latestReleaseUrl;
    private string? _portableZipUrl;
    private string? _setupUrl;

    /// <summary>
    /// Asks GitHub for the newest release of the fork. Read-only by design: the button becomes
    /// "download" and opens the release page — the app never replaces its own executable.
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates) return;

        IsCheckingForUpdates = true;
        UpdateAvailable = false;
        UpdateStatusText = _localization.GetString("CheckingForUpdates");

        try
        {
            var result = await _updateService.CheckForUpdateAsync();

            if (!result.Succeeded)
            {
                UpdateStatusText = _localization.Format("UpdateCheckFailed", result.Error ?? string.Empty);
                return;
            }

            _latestReleaseUrl = result.ReleaseUrl;
            _portableZipUrl = result.PortableZipUrl;
            _setupUrl = result.SetupUrl;

            if (result.UpdateAvailable)
            {
                UpdateAvailable = true;
                UpdateStatusText = _localization.Format("UpdateAvailable", result.LatestVersion ?? string.Empty);
            }
            else
            {
                UpdateStatusText = _localization.Format("UpToDate", result.CurrentVersion);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// Downloads the new build and applies it. Installed copies run the setup silently (it lives
    /// under %LocalAppData%, so no UAC); portable copies unpack next to the exe and hand off to a
    /// tiny helper that swaps the files after this process exits. The user asked for it — nothing
    /// of this happens on its own.
    /// </summary>
    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (IsInstallingUpdate) return;

        var installed = _updateService.IsInstalledCopy;

        var url = installed ? _setupUrl : _portableZipUrl;
        if (string.IsNullOrEmpty(url))
        {
            // A release without the asset we need: fall back to the page.
            await OpenReleasePageAsync();
            return;
        }

        IsInstallingUpdate = true;
        UpdateStatusText = _localization.GetString("DownloadingUpdate");

        try
        {
            var progress = new Progress<double>(value =>
            {
                var percent = (int)Math.Round(value * 100);
                UpdateStatusText = _localization.Format("DownloadingUpdate", percent);
            });

            var file = await _updateService.DownloadAsync(url, progress);

            if (installed)
            {
                UpdateStatusText = _localization.GetString("InstallingUpdate");
                _updateService.RunInstaller(file);
                await Task.Delay(1200);
                RequestShutdown();
                return;
            }

            UpdateStatusText = _localization.GetString("InstallingUpdate");
            var helper = await _updateService.StagePortableUpdateAsync(file);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{helper}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            await Task.Delay(1200);
            RequestShutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError("[Update] Install failed", ex);
            UpdateStatusText = _localization.Format("UpdateFailed", ex.Message);
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

    /// <summary>
    /// Closes the main window so the update helper (or Inno) can replace the files; the window's
    /// close path is what flushes playback progress and shuts the services down.
    /// </summary>
    private void RequestShutdown()
    {
        try
        {
            var mainWindow = App.Services.GetService(typeof(MainWindow)) as MainWindow;
            mainWindow?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Update] Could not close the window: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task OpenReleasePageAsync()
    {
        if (string.IsNullOrEmpty(_latestReleaseUrl)) return;

        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_latestReleaseUrl));
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Update] Could not open the release page: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            var dir = NineLivesAudio.Helpers.AppPaths.DataRoot;
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
        }
    }

    private void LoadDoctorInfo()
    {
        DataDirectory = NineLivesAudio.Helpers.AppPaths.Describe();
        SettingsFilePath = _settingsService.SettingsFilePath;
        SettingsSource = _settingsService.SettingsSource;
        TokenSource = _settingsService.TokenSource;
        LastLoadedAt = _settingsService.LastLoadedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? _localization.GetString("Never");
        SessionId = _logger.SessionId;

        var stored = _settingsService.Settings;
        PlaybackFlags = $"skip intro {stored.SkipIntroSeconds}s · skip outro {stored.SkipOutroSeconds}s · " +
                        $"progress {(stored.PlayerChapterMode ? "chapter" : "book")}";

        if (!string.IsNullOrEmpty(_settingsService.Settings.ServerUrl))
        {
            try { ServerHost = new Uri(_settingsService.Settings.ServerUrl).Host; }
            catch { ServerHost = _settingsService.Settings.ServerUrl; }
        }
        else
        {
            ServerHost = _localization.GetString("NotConfigured");
        }
    }

    /// <summary>NumberBox can hand back NaN or negative values — clamp to 0..600 seconds.</summary>
    public static int ToSeconds(double value)
        => double.IsNaN(value) || value <= 0 ? 0 : (int)Math.Round(Math.Min(value, 600));

    // ── Auto-save ───────────────────────────────────────────────────────────
    // Nothing on this page needs the "save" button: every control persists itself and puts
    // its own result right underneath. Text boxes debounce (400 ms) so typing a URL does not
    // hit the disk per keystroke; toggles write immediately.

    private CancellationTokenSource? _saveCts;

    /// <summary>Copies the UI state into the settings object that every service reads.</summary>
    private void ApplyToSettings()
    {
        var settings = _settingsService.Settings;

        settings.ServerUrl = (ServerUrl ?? string.Empty).Trim();
        settings.Username = Username ?? string.Empty;
        settings.DownloadPath = DownloadPath ?? string.Empty;
        settings.AutoDownloadCovers = AutoDownloadCovers;
        settings.PlaybackSpeed = PlaybackSpeed;
        settings.SkipIntroSeconds = ToSeconds(SkipIntroSeconds);
        settings.SkipOutroSeconds = ToSeconds(SkipOutroSeconds);
        settings.PlayerChapterMode = PlayerChapterMode;
        settings.AutoSyncProgress = AutoSyncProgress;
        settings.SyncIntervalMinutes = SyncIntervalMinutes;
        settings.StartMinimized = StartMinimized;
        settings.MinimizeToTray = MinimizeToTray;
        settings.Volume = Volume;
        settings.Theme = Theme;
        settings.AllowSelfSignedCertificates = AllowSelfSignedCertificates;
        settings.DiagnosticsMode = DiagnosticsMode;
        settings.Language = Language;
    }

    /// <summary>"✓ Saved · 5 min" — the note also states the value that just took effect.</summary>
    private static string Detail(string saved, string? detail)
        => string.IsNullOrWhiteSpace(detail) ? saved : saved + " · " + detail;

    private void AutoSave(Action<string> report, int delayMs = 0)
    {
        if (_isLoadingSettings) return;

        ApplyToSettings();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _saveCts, cts);
        if (previous != null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        _ = PersistAsync(cts.Token, report, delayMs);
    }

    private async Task PersistAsync(CancellationToken token, Action<string> report, int delayMs)
    {
        try
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, token);

            await _settingsService.SaveSettingsAsync();

            report("✓ " + _localization.GetString("SavedInline"));
            LoadDoctorInfo();
            _logger.LogDebug($"[Settings] Auto-saved (intro={_settingsService.Settings.SkipIntroSeconds}s, " +
                             $"outro={_settingsService.Settings.SkipOutroSeconds}s, theme={_settingsService.Settings.Theme})");
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer edit
        }
        catch (Exception ex)
        {
            report("⚠ " + _localization.GetString("SaveFailedInline"));
            _logger.LogError("[Settings] Auto-save failed", ex);
        }
    }

    // ── Per-setting change hooks: apply the effect, then persist ───────────

    partial void OnSpeedSelectedIndexChanged(int value)
    {
        if (_syncingIndexes) return;

        if (value < 0)
        {
            _syncingIndexes = true;
            SpeedSelectedIndex = IndexForSpeed(PlaybackSpeed);
            _syncingIndexes = false;
            return;
        }

        PlaybackSpeed = SpeedForIndex(value);
    }

    partial void OnSyncIntervalSelectedIndexChanged(int value)
    {
        if (_syncingIndexes) return;

        if (value < 0)
        {
            _syncingIndexes = true;
            SyncIntervalSelectedIndex = IndexForInterval(SyncIntervalMinutes);
            _syncingIndexes = false;
            return;
        }

        SyncIntervalMinutes = IntervalForIndex(value);
    }

    partial void OnThemeSelectedIndexChanged(int value)
    {
        if (_syncingIndexes) return;

        if (value < 0)
        {
            _syncingIndexes = true;
            ThemeSelectedIndex = IndexForTheme(Theme);
            _syncingIndexes = false;
            return;
        }

        Theme = ThemeForIndex(value);
    }

    partial void OnSkipIntroSecondsChanged(double value)
    {
        var clamped = ToSeconds(value);
        if (Math.Abs(value - clamped) > 0.001)
        {
            SkipIntroSeconds = clamped;
            return;
        }

        AutoSave(s => SkipIntroStatus = s, 400);
    }

    partial void OnSkipOutroSecondsChanged(double value)
    {
        var clamped = ToSeconds(value);
        if (Math.Abs(value - clamped) > 0.001)
        {
            SkipOutroSeconds = clamped;
            return;
        }

        AutoSave(s => SkipOutroStatus = s, 400);
    }

    partial void OnPlayerChapterModeChanged(bool value) => AutoSave(s => ChapterModeStatus = s);

    partial void OnSelectedServerChanged(SavedServer? value)
    {
        OnPropertyChanged(nameof(HasSelectedServer));
        if (_isLoadingSettings || value == null) return;

        // Selecting the server that is already active must not restart anything.
        if (SavedServer.Normalize(ServerUrl) == SavedServer.Normalize(value.Url)
            && string.Equals(Username ?? string.Empty, value.Username ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            return;

        _ = SwitchToServerAsync(value);
    }

    private void RefreshSavedServers()
    {
        var previous = _isLoadingSettings;
        _isLoadingSettings = true;
        try
        {
            var currentUrl = !string.IsNullOrWhiteSpace(_settingsService.Settings.ServerUrl)
                ? _settingsService.Settings.ServerUrl
                : (_apiService.ServerUrl ?? string.Empty);
            if (IsConnected && !string.IsNullOrWhiteSpace(currentUrl))
            {
                var list = _settingsService.Settings.SavedServers ?? new List<SavedServer>();
                if (!list.Any(s => SavedServer.Normalize(s.Url) == SavedServer.Normalize(currentUrl)))
                {
                    _settingsService.RememberServer(currentUrl, _settingsService.Settings.Username);
                    _ = _settingsService.SaveSettingsAsync();
                }
            }

            SavedServers.Clear();
            foreach (var server in (_settingsService.Settings.SavedServers ?? new List<SavedServer>()))
                SavedServers.Add(server);

            SelectedServer = SavedServers.FirstOrDefault(s =>
                SavedServer.Normalize(s.Url) == SavedServer.Normalize(currentUrl));
            OnPropertyChanged(nameof(HasSelectedServer));
            if (!IsAddingServer)
                ServerName = SelectedServer?.Name ?? string.Empty;
        }
        finally
        {
            _isLoadingSettings = previous;
        }
    }

    [RelayCommand]
    private void StartAddServer()
    {
        _previousServerUrl = ServerUrl;
        _previousUsername = Username;
        _previousServerName = ServerName;
        IsAddingServer = true;
        ServerName = string.Empty;
        ServerUrl = string.Empty;
        Username = string.Empty;
        Password = string.Empty;
        ServerNameStatus = string.Empty;
        ServerUrlStatus = string.Empty;
        UsernameStatus = string.Empty;
        ServerSwitchStatus = string.Empty;
        _logger.Log("[Settings] Entered Add Server mode");
    }

    [RelayCommand]
    private void CancelAddServer()
    {
        IsAddingServer = false;
        ServerName = _previousServerName;
        ServerUrl = _previousServerUrl;
        Username = _previousUsername;
        Password = string.Empty;
        ServerNameStatus = string.Empty;
        ServerUrlStatus = string.Empty;
        UsernameStatus = string.Empty;
        _logger.Log("[Settings] Cancelled Add Server mode");
    }

    [RelayCommand]
    private async Task DeleteSelectedServerAsync()
    {
        var target = SelectedServer;
        if (target == null) return;

        var isCurrent = SavedServer.Normalize(ServerUrl) == SavedServer.Normalize(target.Url)
                        && string.Equals(Username ?? string.Empty, target.Username ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (isCurrent && IsConnected)
            await DisconnectAsync();

        _settingsService.RemoveServer(target.Url, target.Username);
        await _settingsService.SaveSettingsAsync();

        if (isCurrent)
        {
            ServerName = string.Empty;
            ServerUrl = string.Empty;
            Username = string.Empty;
            await _database.ClearAllDataAsync();
            try
            {
                if (Directory.Exists(AppPaths.CoversDirectory))
                {
                    foreach (var file in Directory.GetFiles(AppPaths.CoversDirectory))
                        File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Covers] Failed to clear covers: {ex.Message}");
            }
            _syncService.NotifyDataCleared();
        }

        RefreshSavedServers();
        _logger.Log($"[Settings] Removed saved server {target.Url} (wasCurrent={isCurrent})");
    }

    partial void OnServerNameChanged(string value)
    {
        if (_isLoadingSettings || IsAddingServer || SelectedServer == null) return;
        SelectedServer.Name = value?.Trim() ?? string.Empty;
        AutoSave(s =>
        {
            ServerNameStatus = s;
            RefreshSavedServers();
        }, 500);
    }

    /// <summary>
    /// Switches the active server/account: the token belonging to that server is loaded from
    /// its own file, so switching back does not require typing the password again.
    /// </summary>
    private async Task SwitchToServerAsync(SavedServer server)
    {
        try
        {
            ServerUrl = server.Url;
            Username = server.Username;
            Password = string.Empty;

            ApplyToSettings();
            _settingsService.RememberServer(server.Url, server.Username);
            await _settingsService.SaveSettingsAsync();

            var authenticated = await _apiService.ReloadCredentialsAsync();
            IsAddingServer = false;
            IsConnected = authenticated;
            ConnectionStatus = authenticated
                ? _localization.Format("ConnectedTo", server.Url)
                : _localization.GetString("NotConnectedStatus");

            if (authenticated)
                ServerSwitchStatus = _localization.Format("ServerSwitched", server.DisplayName);
            else
                ServerSwitchStatus = _localization.Format("ServerSwitchedNeedsLogin", server.DisplayName);

            _logger.Log($"[Settings] Switched server to {server.Url} (authenticated={authenticated})");
            LoadDoctorInfo();

            if (authenticated)
                _ = RunInitialSyncAsync();
        }
        catch (Exception ex)
        {
            ServerSwitchStatus = _localization.GetString("SaveFailedInline");
            _logger.LogError("[Settings] Server switch failed", ex);
        }
    }

    partial void OnServerUrlChanged(string value)
    {
        if (IsAddingServer) return;
        AutoSave(s => ServerUrlStatus = Detail(s, value), 600);
    }

    partial void OnUsernameChanged(string value)
    {
        if (IsAddingServer) return;
        AutoSave(s => UsernameStatus = s, 600);
    }

    partial void OnDownloadPathChanged(string value) =>
        AutoSave(s => DownloadPathStatus = Detail(s, value), 600);

    partial void OnAutoDownloadCoversChanged(bool value) => AutoSave(s => AutoDownloadCoversStatus = s);

    partial void OnPlaybackSpeedChanged(double value)
    {
        if (!_syncingIndexes)
        {
            _syncingIndexes = true;
            SpeedSelectedIndex = IndexForSpeed(value);
            _syncingIndexes = false;
        }

        // The player follows immediately, not on the next book.
        _ = _playbackService.SetPlaybackSpeedAsync((float)value);
        AutoSave(s => PlaybackSpeedStatus = Detail(s, value.ToString("0.##") + "x"), 300);
    }

    partial void OnVolumeChanged(double value)
    {
        _ = _playbackService.SetVolumeAsync((float)value);
        AutoSave(s => VolumeStatus = Detail(s, ((int)Math.Round(value * 100)).ToString() + "%"), 300);
    }

    partial void OnAutoSyncProgressChanged(bool value)
    {
        _ = _syncService.ApplyScheduleAsync();
        AutoSave(s => AutoSyncStatus = s);
    }

    partial void OnSyncIntervalMinutesChanged(int value)
    {
        if (!_syncingIndexes)
        {
            _syncingIndexes = true;
            SyncIntervalSelectedIndex = IndexForInterval(value);
            _syncingIndexes = false;
        }

        _ = _syncService.ApplyScheduleAsync();
        AutoSave(s => SyncIntervalStatus = Detail(s, value + " min"));
    }

    partial void OnStartMinimizedChanged(bool value) => AutoSave(s => StartMinimizedStatus = s);

    partial void OnMinimizeToTrayChanged(bool value) => AutoSave(s => MinimizeToTrayStatus = s);

    partial void OnThemeChanged(string value)
    {
        if (!_syncingIndexes)
        {
            _syncingIndexes = true;
            ThemeSelectedIndex = IndexForTheme(value);
            _syncingIndexes = false;
        }

        ThemeService.Apply(value);
        AutoSave(s => ThemeStatus = Detail(s, value));
    }

    partial void OnLanguageChanged(string value) => AutoSave(s => LanguageStatus = s);

    partial void OnAllowSelfSignedCertificatesChanged(bool value) => AutoSave(s => SelfSignedStatus = s);

    partial void OnDiagnosticsModeChanged(bool value)
    {
        _logger.SetDiagnosticsMode(value);
        AutoSave(s => DiagnosticsStatus = s);
    }

    private static string GetDefaultDownloadPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "AudioBookshelf");
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            ErrorMessage = _localization.GetString("EnterServerUrl");
            HasError = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = _localization.GetString("EnterCredentials");
            HasError = true;
            return;
        }

        try
        {
            IsConnecting = true;
            HasError = false;
            HasSuccess = false;
            ConnectionStatus = _localization.GetString("Connecting");

            var success = await _apiService.LoginAsync(ServerUrl, Username, Password);

            if (success)
            {
                IsAddingServer = false;
                IsConnected = true;
                ConnectionStatus = _localization.Format("ConnectedTo", ServerUrl);
                SuccessMessage = _localization.GetString("ConnectedOk");
                HasSuccess = true;
                Password = string.Empty; // Clear password from UI

                _settingsService.RememberServer(ServerUrl, Username, ServerName);
                await SaveSettingsAsync();
                RefreshSavedServers();
                LoadDoctorInfo();

                // Adding a server used to leave the shelf and every progress entry empty until
                // the next 5-minute tick (or a restart). Pull the server data right away.
                await RunInitialSyncAsync();
            }
            else
            {
                IsConnected = false;
                ConnectionStatus = _localization.GetString("ConnectionFailed");
                ErrorMessage = _apiService.LastError ?? _localization.GetString("LoginFailed");
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = _localization.GetString("ConnectionFailed");
            ErrorMessage = _localization.Format("ConnectionError", ex.Message);
            HasError = true;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>
    /// Syncs libraries + progress right after a successful connect and reports how it went.
    /// Runs in the background so the settings page stays usable while the first download happens.
    /// </summary>
    private async Task RunInitialSyncAsync()
    {
        try
        {
            IsSyncingNow = true;
            SuccessMessage = _localization.GetString("FirstSyncRunning");
            HasSuccess = true;

            await _syncService.SyncNowAsync();
            LoadDoctorInfo();

            SuccessMessage = _localization.GetString("FirstSyncDone");
            HasSuccess = true;
            _logger.Log("[Settings] Initial sync after connect finished");
        }
        catch (Exception ex)
        {
            _logger.LogError("[Settings] Initial sync after connect failed", ex);
            ErrorMessage = _localization.Format("SyncFailed", ex.Message);
            HasError = true;
        }
        finally
        {
            IsSyncingNow = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            await _apiService.LogoutAsync();
            IsConnected = false;
            ConnectionStatus = _localization.GetString("NotConnectedStatus");
            SuccessMessage = _localization.GetString("DisconnectedOk");
            HasSuccess = true;
            LoadDoctorInfo();
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("DisconnectError", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!IsConnected)
        {
            ErrorMessage = _localization.GetString("NotConnectedFirst");
            HasError = true;
            return;
        }

        try
        {
            HasError = false;
            HasSuccess = false;

            var valid = await _apiService.ValidateTokenAsync();

            if (valid)
            {
                SuccessMessage = _localization.GetString("TestOk");
                HasSuccess = true;
            }
            else
            {
                ErrorMessage = _localization.GetString("TestFailed");
                HasError = true;
                IsConnected = false;
                ConnectionStatus = _localization.GetString("TokenExpired");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("TestError", ex.Message);
            HasError = true;
        }
    }

    /// <summary>
    /// Safety net for the connect flow (it changes server + credentials at once). The
    /// settings page itself has no Save button: individual controls persist themselves.
    /// </summary>
    private async Task SaveSettingsAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;

            ApplyToSettings();
            await _settingsService.SaveSettingsAsync();

            SuccessMessage = _localization.GetString("SettingsSaved");
            HasSuccess = true;

            LoadDoctorInfo();
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("SettingsSaveFailed", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;

            await _database.ClearAllDataAsync();

            // Clear what is actually on disk too — covers and cached tracks used to survive, so
            // "clear local cache" only looked like it worked.
            CoverImageService.ClearDiskCache();
            await _streamCache.ClearAsync();

            SuccessMessage = _localization.GetString("CacheCleared");
            HasSuccess = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("CacheClearFailed", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ManualSyncAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;

            if (!_apiService.IsAuthenticated)
            {
                ErrorMessage = _localization.GetString("ConnectFirst");
                HasError = true;
                return;
            }

            SuccessMessage = _localization.GetString("Syncing");
            HasSuccess = true;

            // The user asked explicitly: don't let the background dedupe swallow it.
            await _syncService.SyncLibrariesAsync(force: true);
            await _syncService.SyncNowAsync();

            SuccessMessage = _localization.GetString("SyncDone");
            HasSuccess = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("SyncFailed", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ResetConnectionAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;

            await _apiService.LogoutAsync();
            await _settingsService.ClearAuthTokenAsync();

            IsConnected = false;
            ConnectionStatus = _localization.GetString("NotConnectedStatus");
            SuccessMessage = _localization.GetString("ResetDone");
            HasSuccess = true;

            LoadDoctorInfo();
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ResetFailed", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        try
        {
            HasError = false;
            HasSuccess = false;
            var zipPath = await _logger.ExportLogsAsync();
            SuccessMessage = _localization.Format("LogsExported", zipPath);
            HasSuccess = true;

            // Open the containing folder
            var dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ExportFailed", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var logDir = NineLivesAudio.Helpers.AppPaths.LogsDirectory;
            Directory.CreateDirectory(logDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("OpenLogFailed", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsService.SettingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("OpenSettingsFailed", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ApplyLanguageAsync()
    {
        await _localization.SetLanguageAsync(Language);
        // Refresh status text in the new language
        if (IsConnected)
            ConnectionStatus = _localization.Format("ConnectedTo", _apiService.ServerUrl);
        else
            ConnectionStatus = _localization.GetString("NotConnectedStatus");
    }

    [RelayCommand]
    private void DismissError()
    {
        HasError = false;
        ErrorMessage = string.Empty;
    }

}
