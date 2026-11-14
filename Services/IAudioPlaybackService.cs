using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface IAudioPlaybackService
{
    // Playback events migrated to WeakReferenceMessenger:
    // PlaybackStateChangedMessage, PositionChangedMessage, TrackChangedMessage, ChapterChangedMessage

    Task<bool> LoadAudioBookAsync(AudioBook audioBook);
    Task PlayAsync();
    Task PauseAsync();
    Task StopAsync();
    Task SeekAsync(TimeSpan position);
    Task SetVolumeAsync(float volume);
    Task SetPlaybackSpeedAsync(float speed);

    PlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    float Volume { get; }
    float PlaybackSpeed { get; }
    AudioBook? CurrentAudioBook { get; }

    /// <summary>True if currently playing from a locally downloaded file.</summary>
    bool IsPlayingLocalFile { get; }

    /// <summary>Current track index in multi-file audiobook (0-based).</summary>
    int CurrentTrackIndex { get; }

    /// <summary>Total number of tracks in current audiobook.</summary>
    int TotalTracks { get; }

    // Chapter support
    List<Chapter> Chapters { get; }
    Chapter? CurrentChapter { get; }
    int CurrentChapterIndex { get; }

    /// <summary>Seek to the first second of the given chapter (0-based).</summary>
    Task SeekToChapterAsync(int chapterIndex);
}

public enum PlaybackState
{
    Stopped,
    Loading,
    Playing,
    Paused,
    Buffering
}

public class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackState State { get; set; }
    public PlaybackState OldState { get; set; }
    public string? ErrorMessage { get; set; }
}
