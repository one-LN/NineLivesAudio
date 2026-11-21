namespace NineLivesAudio.Helpers;

/// <summary>
/// Shared logic for computing audiobook download paths and sanitizing filenames.
/// Used by DownloadService, SyncService, and PlaybackSourceResolver to ensure
/// consistent path resolution.
/// </summary>
public static class DownloadPathHelper
{
    private const string DefaultSubfolder = "AudioBookshelf";

    public static string GetBasePath(string? configuredDownloadPath)
    {
        if (!string.IsNullOrEmpty(configuredDownloadPath))
            return configuredDownloadPath;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), DefaultSubfolder);
    }

    public static string GetDownloadFolderName(string title, string? author)
    {
        var cleanAuthor = string.IsNullOrWhiteSpace(author) || author == "Unknown Author"
            ? null : author;

        string folderName;
        if (cleanAuthor != null)
            folderName = SanitizeFileName($"{cleanAuthor} - {title}");
        else
            folderName = SanitizeFileName(title);

        return string.IsNullOrWhiteSpace(folderName) ? string.Empty : folderName;
    }

    public static string GetDownloadPath(string? configuredDownloadPath, string title, string? author, string bookId)
    {
        var basePath = GetBasePath(configuredDownloadPath);
        var folderName = GetDownloadFolderName(title, author);
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = bookId;
        return Path.Combine(basePath, folderName);
    }

    public static string GetLegacyDownloadPath(string? configuredDownloadPath, string bookId)
    {
        return Path.Combine(GetBasePath(configuredDownloadPath), bookId);
    }

    /// <summary>
    /// Resolves the actual download directory on disk, checking both the logical
    /// (Author - Title) path and the legacy (bookId) path.
    /// Returns null if neither exists.
    /// </summary>
    public static string? ResolveExistingPath(string? configuredDownloadPath, string title, string? author, string bookId)
    {
        var downloadPath = GetDownloadPath(configuredDownloadPath, title, author, bookId);
        if (Directory.Exists(downloadPath)) return downloadPath;

        var legacyPath = GetLegacyDownloadPath(configuredDownloadPath, bookId);
        if (Directory.Exists(legacyPath)) return legacyPath;

        return null;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }
}
