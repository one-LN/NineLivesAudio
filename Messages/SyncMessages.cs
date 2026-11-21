using CommunityToolkit.Mvvm.Messaging.Messages;
using NineLivesAudio.Services;

namespace NineLivesAudio.Messages;

/// <summary>
/// Sent when a sync operation begins.
/// Replaces ISyncService.SyncStarted event.
/// </summary>
public sealed class SyncStartedMessage : ValueChangedMessage<SyncEventArgs>
{
    public SyncStartedMessage(SyncEventArgs value) : base(value) { }
}

/// <summary>
/// Sent when a sync operation completes successfully.
/// Replaces ISyncService.SyncCompleted event.
/// </summary>
public sealed class SyncCompletedMessage : ValueChangedMessage<SyncEventArgs>
{
    public SyncCompletedMessage(SyncEventArgs value) : base(value) { }
}

/// <summary>
/// Sent when a sync operation fails.
/// Replaces ISyncService.SyncFailed event.
/// </summary>
public sealed class SyncFailedMessage : ValueChangedMessage<SyncErrorEventArgs>
{
    public SyncFailedMessage(SyncErrorEventArgs value) : base(value) { }
}
