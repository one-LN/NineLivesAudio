using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NineLivesAudio.Services;
using NineLivesAudio.ViewModels;

namespace NineLivesAudio.Views;

public sealed partial class StatsPage : Page
{
    private readonly ILocalizationService _localization;

    public StatsViewModel ViewModel { get; }

    public StatsPage()
    {
        ViewModel = App.Services.GetRequiredService<StatsViewModel>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        this.InitializeComponent();

        this.Unloaded += Page_Unloaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalization();

        _localization.LanguageChanged -= ApplyLocalization;
        _localization.LanguageChanged += ApplyLocalization;

        await ViewModel.InitializeAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _localization.LanguageChanged -= ApplyLocalization;
    }

    private void ApplyLocalization()
    {
        if (StatsTitleText != null) StatsTitleText.Text = _localization.GetString("Stats");
        if (TodayLabel != null) TodayLabel.Text = _localization.GetString("StatsToday");
        if (WeekLabel != null) WeekLabel.Text = _localization.GetString("StatsWeek");
        if (MonthLabel != null) MonthLabel.Text = _localization.GetString("StatsMonth");
        if (AllTimeLabel != null) AllTimeLabel.Text = _localization.GetString("StatsAllTime");
        if (LibrarySummaryTitle != null) LibrarySummaryTitle.Text = _localization.GetString("StatsShelf");
        if (StatsEmptyHint != null) StatsEmptyHint.Text = _localization.GetString("StatsEmptyHint");
        if (StatsNote != null) StatsNote.Text = _localization.GetString("StatsLocalNote");
    }
}
