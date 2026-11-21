using CommunityToolkit.Mvvm.Messaging.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;

namespace NineLivesAudio.Messages;

/// <summary>
/// Sent to request navigation to the book detail page.
/// </summary>
public sealed class NavigateToBookDetailMessage : ValueChangedMessage<AudioBook>
{
    public NavigateToBookDetailMessage(AudioBook value) : base(value) { }
}

/// <summary>
/// Sent when a notification should be displayed to the user.
/// Replaces INotificationService.NotificationRequested event.
/// </summary>
public sealed class NotificationRequestedMessage : ValueChangedMessage<NotificationEventArgs>
{
    public NotificationRequestedMessage(NotificationEventArgs value) : base(value) { }
}

/// <summary>
/// Sent when connectivity state changes (online/offline, server reachable).
/// Replaces IConnectivityService.ConnectivityChanged event.
/// </summary>
public sealed class ConnectivityChangedMessage : ValueChangedMessage<ConnectivityChangedEventArgs>
{
    public ConnectivityChangedMessage(ConnectivityChangedEventArgs value) : base(value) { }
}

/// <summary>
/// Sent when a server reconnection attempt completes.
/// Replaces IAudioBookshelfApiService.ReconnectionAttempted event.
/// </summary>
public sealed class ReconnectionAttemptedMessage : ValueChangedMessage<ReconnectionEventArgs>
{
    public ReconnectionAttemptedMessage(ReconnectionEventArgs value) : base(value) { }
}

/// <summary>
/// Sent when app initialization state changes.
/// Replaces IAppInitializer.StateChanged event.
/// </summary>
public sealed class InitStateChangedMessage : ValueChangedMessage<InitState>
{
    public InitStateChangedMessage(InitState value) : base(value) { }
}
