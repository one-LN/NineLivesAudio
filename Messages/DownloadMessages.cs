using CommunityToolkit.Mvvm.Messaging.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;

namespace NineLivesAudio.Messages;

/// <summary>
/// Sent when download progress updates for an active download.
/// Replaces IDownloadService.DownloadProgressChanged event.
/// </summary>
public sealed class DownloadProgressChangedMessage : ValueChangedMessage<DownloadProgressEventArgs>
{
    public DownloadProgressChangedMessage(DownloadProgressEventArgs value) : base(value) { }
}

/// <summary>
/// Sent when a download completes successfully.
/// Replaces IDownloadService.DownloadCompleted event.
/// </summary>
public sealed class DownloadCompletedMessage : ValueChangedMessage<DownloadItem>
{
    public DownloadCompletedMessage(DownloadItem value) : base(value) { }
}

/// <summary>
/// Sent when a download fails.
/// Replaces IDownloadService.DownloadFailed event.
/// </summary>
public sealed class DownloadFailedMessage : ValueChangedMessage<DownloadItem>
{
    public DownloadFailedMessage(DownloadItem value) : base(value) { }
}
