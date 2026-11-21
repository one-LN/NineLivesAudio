namespace NineLivesAudio.Helpers;

/// <summary>
/// Helpers for parsing series metadata (sequence numbers, etc.).
/// </summary>
public static class SeriesHelper
{
    /// <summary>
    /// Parses a series sequence string into a sortable double.
    /// Returns <see cref="double.MaxValue"/> for null, empty, or non-numeric strings
    /// (so they sort to the end).
    /// </summary>
    public static double ParseSequence(string? sequence)
    {
        if (string.IsNullOrEmpty(sequence)) return double.MaxValue;
        if (double.TryParse(sequence, out var num)) return num;
        return double.MaxValue;
    }
}
