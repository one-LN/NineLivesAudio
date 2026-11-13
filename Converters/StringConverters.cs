using Microsoft.UI.Xaml.Data;

namespace NineLivesAudio.Helpers;

public class DownloadStatusToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Models.DownloadStatus status)
        {
            return status switch
            {
                Models.DownloadStatus.Queued => "\uE896",
                Models.DownloadStatus.Downloading => "\uE769",
                Models.DownloadStatus.Paused => "\uE768",
                Models.DownloadStatus.Completed => "\uE73E",
                Models.DownloadStatus.Failed => "\uE783",
                Models.DownloadStatus.Cancelled => "\uE711",
                _ => "\uE896"
            };
        }
        return "\uE896";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class StringToImageSourceConverter : IValueConverter
{
    /// <summary>Decode width used when the binding does not pass one.</summary>
    private const int DefaultDecodeWidth = 300;

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url || string.IsNullOrEmpty(url))
            return null;

        // Go through CoverImageService: it caches per (url, size) and decodes at the
        // requested size, so scrolling a big library no longer re-decodes full-res art
        // for every card that is realised again.
        var decodeWidth = parameter switch
        {
            int i when i > 0 => i,
            string s when int.TryParse(s, out var parsed) && parsed > 0 => parsed,
            _ => DefaultDecodeWidth
        };

        return Services.CoverImageService.LoadCover(url, decodeWidth, decodeWidth);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
