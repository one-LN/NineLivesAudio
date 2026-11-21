using NineLivesAudio.Models;

namespace NineLivesAudio.Services.Playback;

/// <summary>
/// Pure logic for multi-track offset math. No I/O dependencies.
/// </summary>
public class TrackManager : ITrackManager
{
    public TrackList BuildLocalTrackList(AudioBook audioBook, string primaryPath)
    {
        if (audioBook.AudioFiles.Count <= 1)
        {
            return new TrackList { Paths = new[] { primaryPath } };
        }

        var dir = Path.GetDirectoryName(primaryPath);
        if (dir == null)
        {
            return new TrackList { Paths = new[] { primaryPath } };
        }

        var paths = new List<string>();
        var durations = new List<double>();
        double cumulative = 0;

        foreach (var af in audioBook.AudioFiles.OrderBy(f => f.Index))
        {
            var localPath = af.LocalPath;
            if (string.IsNullOrEmpty(localPath))
            {
                var candidate = Path.Combine(dir, Path.GetFileName(af.Filename));
                if (File.Exists(candidate))
                    localPath = candidate;
            }

            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                paths.Add(localPath);
                cumulative += af.Duration.TotalSeconds;
                durations.Add(cumulative);
            }
        }

        if (paths.Count == 0)
        {
            return new TrackList { Paths = new[] { primaryPath } };
        }

        return new TrackList { Paths = paths, CumulativeDurations = durations };
    }

    public IReadOnlyList<double> BuildStreamTrackDurations(IReadOnlyList<AudioStreamInfo> streamTracks)
    {
        var durations = new List<double>(streamTracks.Count);
        double cumulative = 0;
        foreach (var track in streamTracks)
        {
            cumulative += track.Duration;
            durations.Add(cumulative);
        }
        return durations;
    }

    public int DetermineStartingTrack(IReadOnlyList<double> cumulativeDurations, TimeSpan currentTime)
    {
        if (cumulativeDurations.Count == 0 || currentTime.TotalSeconds <= 0)
            return 0;

        double target = currentTime.TotalSeconds;
        for (int i = 0; i < cumulativeDurations.Count; i++)
        {
            if (target < cumulativeDurations[i])
                return i;
        }
        return Math.Max(0, cumulativeDurations.Count - 1);
    }

    public TimeSpan GetWithinTrackOffset(int currentTrackIndex, IReadOnlyList<double> cumulativeDurations, TimeSpan overallPosition)
    {
        if (currentTrackIndex == 0 || cumulativeDurations.Count == 0)
            return overallPosition;

        var previousCumulative = currentTrackIndex > 0 ? cumulativeDurations[currentTrackIndex - 1] : 0;
        var offset = overallPosition.TotalSeconds - previousCumulative;
        return TimeSpan.FromSeconds(Math.Max(0, offset));
    }

    public int FindTrackForPosition(IReadOnlyList<double> cumulativeDurations, TimeSpan position)
    {
        if (cumulativeDurations.Count == 0) return 0;

        double target = position.TotalSeconds;
        for (int i = 0; i < cumulativeDurations.Count; i++)
        {
            if (target < cumulativeDurations[i])
                return i;
        }
        return cumulativeDurations.Count - 1;
    }

    public int FindChapterForPosition(IReadOnlyList<Chapter> chapters, double positionSeconds)
    {
        if (chapters.Count == 0) return -1;

        int lo = 0, hi = chapters.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            var ch = chapters[mid];
            if (positionSeconds < ch.Start)
                hi = mid - 1;
            else if (positionSeconds >= ch.End)
                lo = mid + 1;
            else
                return mid;
        }
        return -1;
    }
}
