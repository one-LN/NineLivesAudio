namespace NineLivesAudio.Helpers;

/// <summary>
/// Maintenance for <c>&lt;data&gt;\stream-cache</c> — the folder that holds the temporary
/// copies written when a book is streamed through the "temp download" fallback.
/// <para>
/// Playback deletes its own file when it stops; this class is the safety net for files left
/// behind by a crash, a force-close or a file that was still locked at the time, and it caps
/// the folder so a long-lived install cannot grow without bound.
/// </para>
/// </summary>
public static class StreamCache
{
    /// <summary>Upper bound for the whole folder (2 GB is roughly 2–3 audiobooks).</summary>
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Files older than this are removed regardless of the total size.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public static string Directory => Path.Combine(AppPaths.DataRoot, "stream-cache");

    /// <summary>
    /// Deletes stale and over-quota files. Returns how many were removed and how many bytes
    /// that freed, so the caller can write a single verifiable log line.
    /// </summary>
    public static (int Removed, long Freed) Purge()
    {
        var removed = 0;
        long freed = 0;

        try
        {
            var dir = Directory;
            if (!System.IO.Directory.Exists(dir))
                return (0, 0);

            var files = new DirectoryInfo(dir).GetFiles("*", SearchOption.TopDirectoryOnly);
            var ordered = files.OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            var now = DateTime.UtcNow;
            long total = ordered.Sum(f => f.Length);

            foreach (var file in ordered)
            {
                var tooOld = now - file.LastWriteTimeUtc > MaxAge;
                var overQuota = total > MaxBytes;

                if (!tooOld && !overQuota)
                    continue;

                try
                {
                    var size = file.Length;
                    file.Delete();
                    removed++;
                    freed += size;
                    total -= size;
                }
                catch (IOException)
                {
                    // Still held open by the player — leave it for the next run.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same: nothing to do beyond skipping.
                }
            }
        }
        catch
        {
            // Maintenance must never break startup.
        }

        return (removed, freed);
    }
}
