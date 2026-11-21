namespace NineLivesAudio.Data;

/// <summary>
/// Compile-time constants for all SQLite table and column names.
/// Eliminates magic strings throughout the data layer.
/// </summary>
public static class Db
{
    public static class Tables
    {
        public const string AudioBooks = "AudioBooks";
        public const string Libraries = "Libraries";
        public const string DownloadItems = "DownloadItems";
        public const string PlaybackProgress = "PlaybackProgress";
        public const string PendingProgressUpdates = "PendingProgressUpdates";
        public const string PendingBookmarks = "PendingBookmarks";
    }

    /// <summary>AudioBooks table columns.</summary>
    public static class AudioBook
    {
        public const string Id = "Id";
        public const string LibraryId = "LibraryId";
        public const string Title = "Title";
        public const string Author = "Author";
        public const string Narrator = "Narrator";
        public const string Description = "Description";
        public const string CoverPath = "CoverPath";
        public const string DurationSeconds = "DurationSeconds";
        public const string AddedAt = "AddedAt";
        public const string AudioFilesJson = "AudioFilesJson";
        public const string CurrentTimeSeconds = "CurrentTimeSeconds";
        public const string Progress = "Progress";
        public const string IsFinished = "IsFinished";
        public const string IsDownloaded = "IsDownloaded";
        public const string LocalPath = "LocalPath";
        public const string SeriesName = "SeriesName";
        public const string SeriesSequence = "SeriesSequence";
        public const string GenresJson = "GenresJson";
        public const string TagsJson = "TagsJson";
        public const string ChaptersJson = "ChaptersJson";
    }

    /// <summary>Libraries table columns.</summary>
    public static class Library
    {
        public const string Id = "Id";
        public const string Name = "Name";
        public const string DisplayOrder = "DisplayOrder";
        public const string Icon = "Icon";
        public const string MediaType = "MediaType";
        public const string FoldersJson = "FoldersJson";
    }

    /// <summary>DownloadItems table columns.</summary>
    public static class Download
    {
        public const string Id = "Id";
        public const string AudioBookId = "AudioBookId";
        public const string Title = "Title";
        public const string Status = "Status";
        public const string TotalBytes = "TotalBytes";
        public const string DownloadedBytes = "DownloadedBytes";
        public const string StartedAt = "StartedAt";
        public const string CompletedAt = "CompletedAt";
        public const string ErrorMessage = "ErrorMessage";
        public const string FilesToDownloadJson = "FilesToDownloadJson";
    }

    /// <summary>PlaybackProgress table columns.</summary>
    public static class Progress
    {
        public const string AudioBookId = "AudioBookId";
        public const string PositionSeconds = "PositionSeconds";
        public const string IsFinished = "IsFinished";
        public const string UpdatedAt = "UpdatedAt";
    }

    /// <summary>PendingProgressUpdates table columns.</summary>
    public static class PendingBookmark
    {
        public const string Id = "Id";
        public const string ItemId = "ItemId";
        public const string Time = "Time";
        public const string Title = "Title";
        public const string Timestamp = "Timestamp";
    }

    public static class PendingProgress
    {
        public const string Id = "Id";
        public const string ItemId = "ItemId";
        public const string CurrentTime = "CurrentTime";
        public const string IsFinished = "IsFinished";
        public const string Timestamp = "Timestamp";
    }
}
