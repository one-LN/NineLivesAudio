namespace NineLivesAudio.Services;

public enum BookmarkCreateResult
{
    /// <summary>Accepted by the server right away.</summary>
    Created,

    /// <summary>Server unreachable — stored locally and uploaded on the next connection.</summary>
    Queued,

    /// <summary>Not stored anywhere (no item, or the local queue itself failed).</summary>
    Failed
}

/// <summary>
/// Bookmark creation that survives being offline. The player used to call the API directly,
/// so tapping "add bookmark" on a plane silently did nothing.
/// </summary>
public interface IBookmarkQueue
{
    Task<BookmarkCreateResult> CreateAsync(string itemId, string title, double time);

    /// <summary>Uploads everything that was queued while offline. Safe to call often.</summary>
    Task DrainAsync();

    /// <summary>How many bookmarks are still waiting (0 when everything is uploaded).</summary>
    int PendingCount { get; }
}
