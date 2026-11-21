namespace NineLivesAudio.Services;

public interface INotificationService
{
    void ShowSuccess(string message, string? title = null);
    void ShowError(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowInfo(string message, string? title = null);
    void Dismiss();
}

public class NotificationEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
    public string? Title { get; set; }
    public NotificationType Type { get; set; }
    public bool ShouldDismiss { get; set; }
}

public enum NotificationType
{
    Success,
    Error,
    Warning,
    Info
}
