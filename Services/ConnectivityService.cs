using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Messages;
using Windows.Networking.Connectivity;

namespace NineLivesAudio.Services;

public class ConnectivityService : IConnectivityService, IDisposable
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ILoggingService _logger;
    private PeriodicTimer? _pingTimer;
    private CancellationTokenSource? _pingCts;
    private bool _isOnline;
    private bool _isServerReachable;

    public bool IsOnline => _isOnline;
    public bool IsServerReachable => _isServerReachable;
    public ConnectivityService(IAudioBookshelfApiService apiService, ILoggingService logger)
    {
        _apiService = apiService;
        _logger = logger;
        _isOnline = CheckNetworkAvailable();
    }

    public Task StartMonitoringAsync()
    {
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        _pingCts = new CancellationTokenSource();
        _pingTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        _ = RunPingLoopAsync(_pingCts.Token);
        _logger.Log("[Connectivity] Monitoring started");
        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync()
    {
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        _pingCts?.Cancel();
        _pingTimer?.Dispose();
        _pingTimer = null;
        _logger.Log("[Connectivity] Monitoring stopped");
        return Task.CompletedTask;
    }

    public async Task<bool> CheckServerReachableAsync()
    {
        if (!_isOnline) return false;
        try
        {
            var reachable = await _apiService.ValidateTokenAsync();
            UpdateState(_isOnline, reachable);
            return reachable;
        }
        catch
        {
            UpdateState(_isOnline, false);
            return false;
        }
    }

    private void OnNetworkStatusChanged(object? sender)
    {
        var wasOnline = _isOnline;
        _isOnline = CheckNetworkAvailable();

        if (wasOnline != _isOnline)
        {
            _logger.Log($"[Connectivity] Network: {(_isOnline ? "Online" : "Offline")}");
            if (!_isOnline)
                UpdateState(false, false);
            else
                _ = CheckServerReachableAsync();
        }
    }

    private async Task RunPingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_pingTimer != null && await _pingTimer.WaitForNextTickAsync(ct))
            {
                if (_isOnline)
                    await CheckServerReachableAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateState(bool online, bool serverReachable)
    {
        var changed = _isOnline != online || _isServerReachable != serverReachable;
        _isOnline = online;
        _isServerReachable = serverReachable;
        if (changed)
        {
            WeakReferenceMessenger.Default.Send(new ConnectivityChangedMessage(
                new ConnectivityChangedEventArgs
                {
                    IsOnline = _isOnline,
                    IsServerReachable = _isServerReachable
                }));
        }
    }

    private static bool CheckNetworkAvailable()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            return profile?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _pingCts?.Cancel();
        _pingTimer?.Dispose();
        _pingCts?.Dispose();
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
    }
}
