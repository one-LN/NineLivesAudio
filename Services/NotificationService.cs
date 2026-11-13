using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Messages;

namespace NineLivesAudio.Services;

public class NotificationService : INotificationService
{
    private readonly ILocalizationService _localization;

    public NotificationService(ILocalizationService localization)
    {
        _localization = localization;
    }

    public void ShowSuccess(string message, string? title = null)
    {
        WeakReferenceMessenger.Default.Send(new NotificationRequestedMessage(new NotificationEventArgs
        {
            Message = message,
            Title = title ?? _localization.GetString("NotifSuccess"),
            Type = NotificationType.Success
        }));
    }

    public void ShowError(string message, string? title = null)
    {
        WeakReferenceMessenger.Default.Send(new NotificationRequestedMessage(new NotificationEventArgs
        {
            Message = message,
            Title = title ?? _localization.GetString("NotifError"),
            Type = NotificationType.Error
        }));
    }

    public void ShowWarning(string message, string? title = null)
    {
        WeakReferenceMessenger.Default.Send(new NotificationRequestedMessage(new NotificationEventArgs
        {
            Message = message,
            Title = title ?? _localization.GetString("NotifWarning"),
            Type = NotificationType.Warning
        }));
    }

    public void ShowInfo(string message, string? title = null)
    {
        WeakReferenceMessenger.Default.Send(new NotificationRequestedMessage(new NotificationEventArgs
        {
            Message = message,
            Title = title,
            Type = NotificationType.Info
        }));
    }

    public void Dismiss()
    {
        WeakReferenceMessenger.Default.Send(new NotificationRequestedMessage(new NotificationEventArgs
        {
            ShouldDismiss = true
        }));
    }
}
