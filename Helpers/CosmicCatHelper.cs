using Windows.UI;

namespace NineLivesAudio.Helpers;

/// <summary>
/// Provides cosmic energy color progression, listening time formatting,
/// and cat silhouette geometry for the Nine Lives page.
/// </summary>
public static class CosmicCatHelper
{
    /// <summary>
    /// Gold-spectrum energy color stops: (hours threshold, R, G, B).
    /// Interpolated linearly between adjacent stops.
    /// Progression: dim gray → nebula teal → sigil gold → brilliant white-gold
    /// </summary>
    private static readonly (double hours, byte r, byte g, byte b)[] ColorStops =
    {
        (0,    0x4A, 0x4A, 0x4A),  // Dim gray
        (1,    0x2C, 0x5F, 0x6E),  // NebulaLight — first glow
        (5,    0x1A, 0x3A, 0x4A),  // NebulaMid — deeper teal
        (10,   0x8A, 0x73, 0x39),  // SigilGoldDim — muted gold
        (25,   0xC5, 0xA5, 0x5A),  // SigilGold — primary gold
        (50,   0xD4, 0xAF, 0x37),  // SigilGoldBright — active gold
        (100,  0xFF, 0xF0, 0xC8),  // Brilliant white-gold
    };

    /// <summary>
    /// Returns a color on the gold energy spectrum based on hours listened.
    /// 0h=dim gray → 1h=nebula teal → 5h=deep teal → 10h=muted gold → 25h=gold → 50h=bright gold → 100h+=white-gold
    /// </summary>
    public static Color GetCosmicEnergyColor(double hoursListened)
    {
        if (hoursListened <= 0)
            return Color.FromArgb(0xFF, ColorStops[0].r, ColorStops[0].g, ColorStops[0].b);

        if (hoursListened >= ColorStops[^1].hours)
            return Color.FromArgb(0xFF, ColorStops[^1].r, ColorStops[^1].g, ColorStops[^1].b);

        for (int i = 0; i < ColorStops.Length - 1; i++)
        {
            if (hoursListened >= ColorStops[i].hours && hoursListened < ColorStops[i + 1].hours)
            {
                double t = (hoursListened - ColorStops[i].hours) /
                           (ColorStops[i + 1].hours - ColorStops[i].hours);
                return Color.FromArgb(0xFF,
                    Lerp(ColorStops[i].r, ColorStops[i + 1].r, t),
                    Lerp(ColorStops[i].g, ColorStops[i + 1].g, t),
                    Lerp(ColorStops[i].b, ColorStops[i + 1].b, t));
            }
        }

        return Color.FromArgb(0xFF, ColorStops[^1].r, ColorStops[^1].g, ColorStops[^1].b);
    }

    private static byte Lerp(byte a, byte b, double t)
        => (byte)(a + (b - a) * Math.Clamp(t, 0, 1));

    /// <summary>
    /// Formats a TimeSpan as a compact listening time string.
    /// Examples: "< 1m", "45m", "3h 22m", "12h 5m"
    /// </summary>
    public static string FormatListeningTime(TimeSpan time)
    {
        if (time.TotalMinutes < 1) return "< 1m";
        if (time.TotalHours < 1) return $"{(int)time.TotalMinutes}m";
        return $"{(int)time.TotalHours}h {time.Minutes}m";
    }

    /// <summary>
    /// XAML Path geometry for a sitting cat silhouette (24×24 viewbox).
    /// Placeholder — can be replaced with polished artwork later.
    /// Features: pointed ears, rounded body, curled tail on the right.
    /// </summary>
    public const string CatSilhouetteGeometry =
        // Body: bottom-left → left side up → left ear peak → head curve → right ear peak → right side down → base
        "M 6,20 L 6,12 C 6,10 7,8 8,7 L 5,2 L 8,5 C 9,4.5 10,4 12,4 " +
        "C 14,4 15,4.5 16,5 L 19,2 L 16,7 C 17,8 18,10 18,12 L 18,20 " +
        "C 18,21 17,22 16,22 L 8,22 C 7,22 6,21 6,20 Z " +
        // Tail: curving out from right hip
        "M 20,18 C 20,16 21,14 22,14 C 23,14 23,15 22,17 C 21,19 20,20 20,18 Z";
}
