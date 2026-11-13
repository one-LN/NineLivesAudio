using NineLivesAudio.Data;

namespace NineLivesAudio.Services;

public class AppInitializer : IAppInitializer
{
    private readonly ILocalDatabase _database;
    private readonly ISettingsService _settingsService;
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ISyncService _syncService;
    private readonly ILoggingService _logger;
    private readonly ILocalizationService _localization;
    private int _running; // 0 = idle, 1 = running

    public InitState State { get; private set; } = InitState.NotStarted;
    public string? ErrorMessage { get; private set; }
    public event EventHandler<InitState>? StateChanged;

    public AppInitializer(
        ILocalDatabase database,
        ISettingsService settingsService,
        IAudioBookshelfApiService apiService,
        ISyncService syncService,
        ILoggingService loggingService,
        ILocalizationService localization)
    {
        _database = database;
        _settingsService = settingsService;
        _apiService = apiService;
        _syncService = syncService;
        _logger = loggingService;
        _localization = localization;
    }

    public async Task InitializeAsync()
    {
        // Guard: prevent concurrent runs (but allow retry after failure)
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        if (State == InitState.Ready)
        {
            Interlocked.Exchange(ref _running, 0);
            return; // Already initialized
        }

        try
        {
            SetState(InitState.Initializing);
            _logger.Log("AppInitializer: starting initialization");

            // Stage 1: Database + Settings in parallel (independent of each other)
            _logger.Log("AppInitializer: initializing database + loading settings (parallel)...");
            var dbTask = _database.InitializeAsync();
            var settingsTask = _settingsService.LoadSettingsAsync();
            await Task.WhenAll(dbTask, settingsTask);
            // Apply persisted UI language now that settings are loaded.
            _localization.RefreshFromSettings();
            _logger.Log($"AppInitializer: database + settings ready (server={_settingsService.Settings.ServerUrl})");

            // Stage 2: Validate token with timeout — transition to Ready quickly
            if (!string.IsNullOrEmpty(_settingsService.Settings.ServerUrl))
            {
                _logger.Log("AppInitializer: validating auth token (8s timeout)...");
                bool valid = false;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var validateTask = _apiService.ValidateTokenAsync();
                    var completed = await Task.WhenAny(validateTask, Task.Delay(Timeout.Infinite, cts.Token));
                    if (completed == validateTask)
                    {
                        valid = await validateTask;
                        _logger.Log($"AppInitializer: token valid={valid}");
                    }
                    else
                    {
                        _logger.LogWarning("AppInitializer: token validation timed out (8s), continuing startup");
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("AppInitializer: token validation timed out, continuing startup");
                }

                // Stage 3: Fire-and-forget sync start — don't block Ready.
                // Started even when validation timed out: SyncService skips work while
                // unauthenticated, so a slow network can no longer disable syncing for
                // the whole session (that used to leave the shelf empty until restart).
                if (_settingsService.Settings.AutoSyncProgress)
                {
                    _logger.Log("AppInitializer: starting sync service (background)");
                    _ = Task.Run(async () =>
                    {
                        try { await _syncService.StartAsync(); }
                        catch (Exception ex) { _logger.LogDebug($"AppInitializer: background sync start failed: {ex.Message}"); }
                    });
                }
            }

            SetState(InitState.Ready);
            _logger.Log("AppInitializer: initialization complete");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError("AppInitializer: initialization failed", ex);
            SetState(InitState.Failed);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private void SetState(InitState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
