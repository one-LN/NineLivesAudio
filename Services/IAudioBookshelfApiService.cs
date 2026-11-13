using NineLivesAudio.Models;

namespace NineLivesAudio.Services;

public interface IAudioBookshelfApiService
{
    bool IsAuthenticated { get; }
    string? ServerUrl { get; }
    string? LastError { get; }

    /// <summary>
    /// True when the most recent request was rejected with HTTP 401, i.e. the stored
    /// token is no longer accepted by the server. Lets the UI show "session expired"
    /// instead of an empty library.
    /// </summary>
    bool LastRequestUnauthorized { get; }

    /// <summary>
    /// Drops the in-memory session (token + Authorization header) so callers stop
    /// hammering the API with a dead token. The saved token is left untouched.
    /// </summary>
    void InvalidateSession();

    // Authentication
    Task<bool> LoginAsync(string serverUrl, string username, string password);
    Task LogoutAsync();
    Task<bool> ValidateTokenAsync();

    // Libraries
    Task<List<Library>> GetLibrariesAsync();
    Task<List<AudioBook>> GetLibraryItemsAsync(string libraryId, int limit = 100, int page = 0);

    // Audiobooks
    Task<AudioBook?> GetAudioBookAsync(string itemId);
    Task<Stream> GetAudioFileStreamAsync(string itemId, string fileIno);
    Task<byte[]?> GetCoverImageAsync(string itemId, int? width = null, int? height = null);

    // Playback & Progress
    Task<PlaybackSessionInfo?> StartPlaybackSessionAsync(string itemId);
    /// <summary>
    /// Pushes listening progress. <paramref name="duration"/> is used to send the
    /// 0..1 <c>progress</c> ratio; pass 0 to let the server compute it.
    /// </summary>
    Task<bool> UpdateProgressAsync(string itemId, double currentTime, bool isFinished = false, double duration = 0);
    Task<bool> SyncSessionProgressAsync(string sessionId, double currentTime, double duration, double timeListened = 0);
    Task ClosePlaybackSessionAsync(string sessionId);

    // User
    Task<UserProgress?> GetUserProgressAsync(string itemId);
    Task<List<UserProgress>> GetAllUserProgressAsync();

    // Bookmarks
    Task<List<Bookmark>> GetBookmarksAsync(string itemId);
    Task<bool> CreateBookmarkAsync(string itemId, string title, double time);
    Task<bool> DeleteBookmarkAsync(string itemId, double time);

    // Reconnection (event migrated to WeakReferenceMessenger: ReconnectionAttemptedMessage)
    Task<bool> TryReconnectAsync();
}

public class ReconnectionEventArgs : EventArgs
{
    public bool Success { get; init; }
    public string? ServerUrl { get; init; }
    public string? ErrorMessage { get; init; }
    public int AttemptNumber { get; init; }
}

public class PlaybackSessionInfo
{
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string? EpisodeId { get; set; }
    public double CurrentTime { get; set; }
    public double Duration { get; set; }
    public string MediaType { get; set; } = "book";
    public List<AudioStreamInfo> AudioTracks { get; set; } = new();
    public List<Chapter> Chapters { get; set; } = new();
}

public class AudioStreamInfo
{
    public int Index { get; set; }
    public string Codec { get; set; } = string.Empty;
    public string? Title { get; set; }
    public double Duration { get; set; }
    public string ContentUrl { get; set; } = string.Empty;
}
