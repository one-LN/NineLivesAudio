namespace NineLivesAudio.Models;

/// <summary>One row of the on-device listening history ("yyyy-MM-dd" + seconds listened).</summary>
public class ListeningDay
{
    public string Date { get; set; } = string.Empty;
    public double Seconds { get; set; }

    public DateTime DateValue =>
        DateTime.TryParseExact(Date, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : DateTime.MinValue;
}
