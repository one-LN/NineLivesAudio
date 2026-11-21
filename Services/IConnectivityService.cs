namespace NineLivesAudio.Services;

public interface IConnectivityService
{
    bool IsOnline { get; }
    bool IsServerReachable { get; }
    Task StartMonitoringAsync();
    Task StopMonitoringAsync();
    Task<bool> CheckServerReachableAsync();
}

public class ConnectivityChangedEventArgs : EventArgs
{
    public bool IsOnline { get; set; }
    public bool IsServerReachable { get; set; }
}
