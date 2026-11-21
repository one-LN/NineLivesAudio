using NineLivesAudio.Models;

namespace NineLivesAudio.Services.Playback;

/// <summary>
/// Pure logic for multi-track audiobook offset calculations.
/// No I/O, no state mutation of external objects — fully testable.
/// </summary>
public interface ITrackManager
{
    /// <summary>
    /// Builds ordered local file paths from an audiobook's audio files.
    /// Returns list of valid local paths and cumulative durations.
    /// </summary>
    TrackList BuildLocalTrackList(AudioBook audioBook, string primaryPath);

    /// <summary>
    /// Builds cumulative duration list from streaming track info.
    /// </summary>
    IReadOnlyList<double> BuildStreamTrackDurations(IReadOnlyList<AudioStreamInfo> streamTracks);

    /// <summary>
    /// Determines which track index to start from, given an overall position.
    /// </summary>
    int DetermineStartingTrack(IReadOnlyList<double> cumulativeDurations, TimeSpan currentTime);

    /// <summary>
    /// Converts an overall audiobook position to a within-track offset.
    /// </summary>
    TimeSpan GetWithinTrackOffset(int currentTrackIndex, IReadOnlyList<double> cumulativeDurations, TimeSpan overallPosition);

    /// <summary>
    /// Determines which track contains the given overall position.
    /// </summary>
    int FindTrackForPosition(IReadOnlyList<double> cumulativeDurations, TimeSpan position);

    /// <summary>
    /// Binary search over sorted chapters to find the one containing the given position.
    /// Returns -1 if no chapter contains the position.
    /// </summary>
    int FindChapterForPosition(IReadOnlyList<Chapter> chapters, double positionSeconds);
}

/// <summary>
/// Immutable result from building a local track list.
/// </summary>
public sealed class TrackList
{
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> CumulativeDurations { get; init; } = Array.Empty<double>();
}
