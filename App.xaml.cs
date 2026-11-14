using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NineLivesAudio.Services;
using NineLivesAudio.Services.Playback;
using NineLivesAudio.ViewModels;
using NineLivesAudio.Views;
using NineLivesAudio.Data;
using NineLivesAudio.Helpers;

namespace NineLivesAudio;

public partial class App : Application
{
    private static IHost? _host;
    private static MiniPlayerWindow? _miniPlayerWindow;

    public static IServiceProvider Services => _host!.Services;

    public App()
    {
        this.InitializeComponent();

        // Migrate legacy "AudioBookshelfApp" folders/vault to "NineLivesAudio"
        LegacyMigrationHelper.MigrateIfNeeded();

        // Runtime data now lives in <exe folder>\data. On the first start of such a build
        // the settings/database/logs from %LOCALAPPDATA% are imported, so the library,
        // saved progress and the server login carry over.
        AppPaths.MigrateLegacyDataIfNeeded();

        // Build dependency injection container
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Logging (register first so other services can use it)
                services.AddSingleton<ILoggingService, LoggingService>();

                // Services - Core
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IAudioBookshelfApiService, AudioBookshelfApiService>();
                services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
                services.AddSingleton<IDownloadService, DownloadService>();
                services.AddSingleton<ISyncService, SyncService>();
                services.AddSingleton<ILocalDatabase, LocalDatabase>();
                services.AddSingleton<IAppInitializer, AppInitializer>();

                // Services - Connectivity & Offline
                services.AddSingleton<IConnectivityService, ConnectivityService>();
                services.AddSingleton<IOfflineProgressQueue, OfflineProgressQueue>();

                // Services - Navigation
                services.AddSingleton<INavigationService, NavigationService>();

                // Localization (must be after SettingsService)
                services.AddSingleton<ILocalizationService, LocalizationService>();

                // Services - UI/UX Enhancements
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<ITrayIconService, TrayIconService>();
                services.AddSingleton<IMetadataNormalizer, MetadataNormalizer>();
                services.AddSingleton<IPlaybackSourceResolver, PlaybackSourceResolver>();
                services.AddSingleton<ILibraryFilterService, LibraryFilterService>();
                services.AddSingleton<ITrackManager, TrackManager>();

                // ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<HomeViewModel>();
                services.AddTransient<LibraryViewModel>();
                services.AddTransient<PlayerViewModel>();
                services.AddTransient<DownloadsViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<BookDetailViewModel>();

                // Main Window
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Set up global exception handlers
        this.UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // CRITICAL: No blocking async calls here.
        // Show the window IMMEDIATELY, then let MainWindow kick off async init.
        ILoggingService? logger = null;
        try
        {
            logger = Services.GetRequiredService<ILoggingService>();
            logger.Log("App.OnLaunched: activating main window (no async blocking)");

            // Hook up localized XAML text (loc:Loc.*) before any page is created.
            Loc.Initialize(
                Services.GetRequiredService<ILocalizationService>(),
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Activate();

            logger.Log("App.OnLaunched: window activated, async init will run from MainWindow.Loaded");
        }
        catch (Exception ex)
        {
            logger?.LogError("FATAL: App.OnLaunched failed", ex);
            System.Diagnostics.Debug.WriteLine($"FATAL: App launch failed: {ex}");
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = Services.GetService<ILoggingService>();
        logger?.LogError("UNHANDLED XAML EXCEPTION", e.Exception);
        FlushLogsBestEffort(logger);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var logger = Services.GetService<ILoggingService>();
        logger?.LogError("UNHANDLED DOMAIN EXCEPTION", e.ExceptionObject as Exception);
        FlushLogsBestEffort(logger);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logger = Services.GetService<ILoggingService>();
        logger?.LogError("UNOBSERVED TASK EXCEPTION", e.Exception);
        FlushLogsBestEffort(logger);
        e.SetObserved();
    }

    private static void FlushLogsBestEffort(ILoggingService? logger)
    {
        // Best-effort synchronous flush with a hard timeout to avoid deadlocks
        // in crash handlers. Never throws.
        try
        {
            if (logger != null)
            {
                var flush = logger.FlushAsync();
                flush.Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch { /* crash path — ignore */ }
    }

    // --- Mini Player Window Management ---

    public static void OpenMiniPlayer()
    {
        if (_miniPlayerWindow != null)
        {
            _miniPlayerWindow.Activate();
            return;
        }

        _miniPlayerWindow = new MiniPlayerWindow();
        _miniPlayerWindow.Closed += (s, e) => _miniPlayerWindow = null;
        _miniPlayerWindow.Activate();
    }

    public static void CloseMiniPlayer()
    {
        _miniPlayerWindow?.Close();
        _miniPlayerWindow = null;
    }
}
