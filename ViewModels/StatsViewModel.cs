using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NineLivesAudio.Data;
using NineLivesAudio.Models;
using NineLivesAudio.Services;

namespace NineLivesAudio.ViewModels;

/// <summary>
/// Statistics page: listening time per day plus a small library overview.
/// <para>
/// The numbers come from the app's own <c>ListeningDaily</c> table (written every 12 s while
/// audio actually plays), so they work offline and are not affected by the server-side
/// session accounting.
/// </para>
/// </summary>
public partial class StatsViewModel : ObservableObject
{
    private const int ChartDays = 14;
    private const double MaxBarHeight = 120;
    private const double MinBarHeight = 4;

    private readonly ILocalDatabase _database;
    private readonly ILocalizationService _localization;

    [ObservableProperty] private string _todayText = "—";
    [ObservableProperty] private string _weekText = "—";
    [ObservableProperty] private string _monthText = "—";
    [ObservableProperty] private string _allTimeText = "—";
    [ObservableProperty] private string _booksSummary = string.Empty;
    [ObservableProperty] private string _chartTitle = string.Empty;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isEmpty = true;

    public ObservableCollection<StatBar> Bars { get; } = new();

    public StatsViewModel(ILocalDatabase database, ILocalizationService localization)
    {
        _database = database;
        _localization = localization;
    }

    public async Task InitializeAsync()
    {
        ChartTitle = _localization.Format("StatsChartTitle", ChartDays);

        var days = await _database.GetListeningDaysAsync(400);

        var today = DateTime.Today;
        var todayKey = today.ToString("yyyy-MM-dd");
        var weekStart = today.AddDays(-6);
        var monthStart = today.AddDays(-29);

        double todaySeconds = 0, weekSeconds = 0, monthSeconds = 0, allSeconds = 0;

        foreach (var day in days)
        {
            allSeconds += day.Seconds;

            if (day.Date == todayKey) todaySeconds += day.Seconds;
            var value = day.DateValue;
            if (value == DateTime.MinValue) continue;
            if (value >= weekStart) weekSeconds += day.Seconds;
            if (value >= monthStart) monthSeconds += day.Seconds;
        }

        TodayText = FormatDuration(todaySeconds);
        WeekText = FormatDuration(weekSeconds);
        MonthText = FormatDuration(monthSeconds);
        AllTimeText = FormatDuration(allSeconds);

        HasData = allSeconds >= 60;
        IsEmpty = !HasData;

        BuildChart(days, today);

        await LoadLibrarySummaryAsync();
    }

    private void BuildChart(List<ListeningDay> days, DateTime today)
    {
        Bars.Clear();

        var byDate = days
            .Where(d => d.DateValue != DateTime.MinValue)
            .GroupBy(d => d.Date)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Seconds));

        var window = new List<ListeningDay>();
        for (var i = ChartDays - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var key = date.ToString("yyyy-MM-dd");
            window.Add(new ListeningDay { Date = key, Seconds = byDate.TryGetValue(key, out var s) ? s : 0 });
        }

        var peak = window.Count == 0 ? 0 : window.Max(d => d.Seconds);

        foreach (var day in window)
        {
            var ratio = peak <= 0 ? 0 : day.Seconds / peak;
            Bars.Add(new StatBar
            {
                Label = day.DateValue.ToString("M/d"),
                Height = day.Seconds <= 0 ? MinBarHeight : Math.Max(MinBarHeight, ratio * MaxBarHeight),
                Tooltip = $"{day.DateValue:M/d} · {FormatDuration(day.Seconds)}"
            });
        }
    }

    private async Task LoadLibrarySummaryAsync()
    {
        try
        {
            var books = await _database.GetAllAudioBooksAsync();
            var finished = books.Count(b => b.IsFinished);
            var inProgress = books.Count(b => !b.IsFinished && b.CurrentTime.TotalSeconds > 60);

            BooksSummary = _localization.Format("StatsLibrarySummary", books.Count, inProgress, finished);
        }
        catch
        {
            BooksSummary = string.Empty;
        }
    }

    /// <summary>"1 小时 23 分" / "23 min" — short form for the stat cards.</summary>
    private string FormatDuration(double seconds)
    {
        if (seconds < 1) return _localization.GetString("StatsZero");

        var span = TimeSpan.FromSeconds(seconds);

        if (span.TotalHours >= 1)
        {
            var hours = (int)span.TotalHours;
            var minutes = span.Minutes;

            if (minutes > 0)
                return _localization.Format("StatsHoursMinutes", hours, minutes);

            return _localization.Format("StatsHours", hours);
        }

        return _localization.Format("StatsMinutes", Math.Max(1, (int)Math.Round(span.TotalMinutes)));
    }
}

/// <summary>One column of the 14-day chart.</summary>
public class StatBar
{
    public string Label { get; set; } = string.Empty;
    public double Height { get; set; }
    public string Tooltip { get; set; } = string.Empty;
}
