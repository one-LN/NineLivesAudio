using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.Concurrent;

namespace NineLivesAudio.Services;

/// <summary>
/// Centralized cover image loading with an in-memory cache and decode-size
/// limits so list scrolling does not repeatedly decode full-resolution art.
/// </summary>
public static class CoverImageService
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();
    private const int MaxCached = 300;

    /// <summary>
    /// Loads a BitmapImage from a cover path URI, returning null on failure.
    /// Results are cached; decode size is capped to the card size.
    /// </summary>
    public static BitmapImage? LoadCover(string? coverPath, int decodeWidth = 400, int decodeHeight = 400)
    {
        if (string.IsNullOrEmpty(coverPath))
            return null;

        var cacheKey = $"{coverPath}|{decodeWidth}x{decodeHeight}";
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var image = new BitmapImage(new Uri(coverPath))
            {
                DecodePixelWidth = decodeWidth,
                DecodePixelHeight = decodeHeight,
            };
            if (Cache.Count < MaxCached)
                Cache[cacheKey] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads a thumbnail-sized BitmapImage by appending width/height query parameters.
    /// Used for mini player and other compact views.
    /// </summary>
    public static BitmapImage? LoadThumbnail(string? coverPath, int width = 100, int height = 100)
    {
        if (string.IsNullOrEmpty(coverPath))
            return null;

        var cacheKey = $"{coverPath}|thumb{width}x{height}";
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var thumbnailUri = coverPath.Contains('?')
                ? $"{coverPath}&width={width}&height={height}"
                : $"{coverPath}?width={width}&height={height}";
            var image = new BitmapImage(new Uri(thumbnailUri))
            {
                DecodePixelWidth = width,
                DecodePixelHeight = height,
            };
            if (Cache.Count < MaxCached)
                Cache[cacheKey] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Clears the cover cache (e.g. on logout / cache clear).</summary>
    public static void ClearCache() => Cache.Clear();
}
