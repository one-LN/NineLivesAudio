using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace NineLivesAudio.Helpers;

/// <summary>Non-empty string → Visible. Used for the inline "saved" notes in Settings.</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

