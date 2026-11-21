using NineLivesAudio.Models;

namespace NineLivesAudio.Data;

public interface ILocalDatabase
{
    Task InitializeAsync();

    // AudioBooks
    Task<List<AudioBook>> GetAllAudioBooksAsync();
    Task<List<AudioBook>> GetAudioBooksByLibraryAsync(string libraryId);
    Task<AudioBook?> GetAudioBookAsync(string id);
    Task SaveAudioBookAsync(AudioBook audioBook);
    Task UpdateAudioBookAsync(AudioBook audioBook);
    Task DeleteAudioBookAsync(string id);

    // Libraries
    Task<List<Library>> GetAllLibrariesAsync();
    Task<Library?> GetLibraryAsync(string id);
    Task SaveLibraryAsync(Library library);
    Task UpdateLibraryAsync(Library library);
    Task DeleteLibraryAsync(string id);

    // Downloads
    Task<List<DownloadItem>> GetAllDownloadItemsAsync();
    Task<DownloadItem?> GetDownloadItemAsync(string id);
    Task SaveDownloadItemAsync(DownloadItem downloadItem);
    Task UpdateDownloadItemAsync(DownloadItem downloadItem);
    Task DeleteDownloadItemAsync(string id);

    // Playback Progress
    Task SavePlaybackProgressAsync(string audioBookId, TimeSpan position, bool isFinished, DateTime? updatedAt = null);
    Task<(TimeSpan position, bool isFinished)?> GetPlaybackProgressAsync(string audioBookId);
    Task<(TimeSpan position, bool isFinished, DateTime updatedAt)?> GetPlaybackProgressWithTimestampAsync(string audioBookId);

    // Nine Lives (recently played)
    Task<List<(AudioBook Book, DateTime LastPlayedAt)>> GetRecentlyPlayedAsync(int limit = 9);

    // Offline Progress Queue
    Task EnqueuePendingProgressAsync(string itemId, double currentTime, bool isFinished);
    Task<List<PendingProgressEntry>> GetPendingProgressAsync();
    Task ClearPendingProgressAsync();
    Task ClearPendingProgressForItemsAsync(IEnumerable<string> itemIds);
    Task<int> GetPendingProgressCountAsync();

    // On-device listening history (Statistics page).
    Task AddListeningSecondsAsync(string dateKey, double seconds);
    Task<List<ListeningDay>> GetListeningDaysAsync(int days);

    // Offline bookmark queue — created while offline, uploaded when the server is back.
    Task EnqueuePendingBookmarkAsync(string itemId, string title, double time);
    Task<List<PendingBookmarkEntry>> GetPendingBookmarksAsync();
    Task DeletePendingBookmarkAsync(long id);

    // Bulk operations
    Task SaveAudioBooksAsync(IEnumerable<AudioBook> audioBooks);
    Task SaveLibrariesAsync(IEnumerable<Library> libraries);
    Task ClearAllDataAsync();
}

public class PendingProgressEntry
{
    public string ItemId { get; set; } = string.Empty;
    public double CurrentTime { get; set; }
    public bool IsFinished { get; set; }
    public DateTime Timestamp { get; set; }
}
