using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace NineLivesAudio.Helpers;

public class HoursToCosmicBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush DefaultBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0x4A, 0x4A));

    // Cache brushes by color to avoid allocating a new brush on every binding update.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Windows.UI.Color, SolidColorBrush> BrushCache = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double hours)
        {
            var color = CosmicCatHelper.GetCosmicEnergyColor(hours);
            return BrushCache.GetOrAdd(color, c => new SolidColorBrush(c));
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a duration string (e.g. "LIGHT", "MEDIUM", "HEAVY") to a themed brush.
/// LIGHT → MistFaint, MEDIUM → Mist, HEAVY → SigilGoldDim
/// </summary>
public class WeightToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush LightBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x72, 0x80));   // MistFaint
    private static readonly SolidColorBrush MediumBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x9B, 0xA4, 0xB5));   // Mist
    private static readonly SolidColorBrush HeavyBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x8A, 0x73, 0x39));   // SigilGoldDim

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string weight)
        {
            return weight switch
            {
                "HEAVY" => HeavyBrush,
                "MEDIUM" => MediumBrush,
                _ => LightBrush,
            };
        }
        return LightBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a life index (0-8) to a Roman numeral label: "LIFE I" through "LIFE IX".
/// </summary>
public class LifeIndexToLabelConverter : IValueConverter
{
    private static readonly string[] RomanNumerals =
        { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int index && index >= 0 && index < RomanNumerals.Length)
        {
            return $"LIFE {RomanNumerals[index]}";
        }
        return "LIFE";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
