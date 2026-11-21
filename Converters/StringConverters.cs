using Microsoft.UI.Xaml.Data;

namespace NineLivesAudio.Helpers;

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
