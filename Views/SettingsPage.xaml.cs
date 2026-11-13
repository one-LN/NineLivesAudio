using NineLivesAudio.Services;
using NineLivesAudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NineLivesAudio.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SolidColorBrush ConnectionStatusColor => ViewModel.IsConnected
        ? (SolidColorBrush)Application.Current.Resources["RitualSuccessBrush"]
        : (SolidColorBrush)Application.Current.Resources["MistFaintBrush"];

    public int SpeedSelectedIndex
    {
        get => ViewModel.PlaybackSpeed switch
        {
            0.5 => 0,
            0.75 => 1,
            1.0 => 2,
            1.25 => 3,
            1.5 => 4,
            1.75 => 5,
            2.0 => 6,
            _ => 2
        };
        set => ViewModel.PlaybackSpeed = value switch
        {
            0 => 0.5,
            1 => 0.75,
            2 => 1.0,
            3 => 1.25,
            4 => 1.5,
            5 => 1.75,
            6 => 2.0,
            _ => 1.0
        };
    }

    public int SyncIntervalSelectedIndex
    {
        get => ViewModel.SyncIntervalMinutes switch
        {
            1 => 0,
            5 => 1,
            10 => 2,
            15 => 3,
            30 => 4,
            _ => 1
        };
        set => ViewModel.SyncIntervalMinutes = value switch
        {
            0 => 1,
            1 => 5,
            2 => 10,
            3 => 15,
            4 => 30,
            _ => 5
        };
    }

    public int ThemeSelectedIndex
    {
        get => ViewModel.Theme switch
        {
            "System" => 0,
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        set => ViewModel.Theme = value switch
        {
            0 => "System",
            1 => "Light",
            2 => "Dark",
            _ => "System"
        };
    }

    private readonly ILocalizationService _localization;

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        this.InitializeComponent();
        this.Unloaded += Page_Unloaded;
    }

    // Guards SelectionChanged while the combo box is being synced from settings.
    private bool _isSyncingLanguage;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        ApplyLocalization();
        SyncLanguageComboBox();
        _localization.LanguageChanged -= ApplyLocalization;
        _localization.LanguageChanged += ApplyLocalization;
    }

    private void SyncLanguageComboBox()
    {
        if (LanguageComboBox == null) return;

        var lang = ViewModel.Language ?? "system";
        _isSyncingLanguage = true;
        try
        {
            for (int i = 0; i < LanguageComboBox.Items.Count; i++)
            {
                if (LanguageComboBox.Items[i] is ComboBoxItem item
                    && string.Equals(item.Tag?.ToString(), lang, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageComboBox.SelectedIndex = i;
                    return;
                }
            }
            LanguageComboBox.SelectedIndex = 0;
        }
        finally
        {
            _isSyncingLanguage = false;
        }
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingLanguage) return;
        if (LanguageComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;

        try
        {
            ViewModel.Language = tag;
            await ViewModel.ApplyLanguageCommand.ExecuteAsync(null);
            // Re-render the option labels themselves in the newly selected language.
            ApplyLocalization();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
            ViewModel.HasError = true;
        }
    }

    private static void LocalizeComboItem(ComboBox combo, int index, string content)
    {
        if (combo.Items.Count > index && combo.Items[index] is ComboBoxItem item)
            item.Content = content;
    }

    private void ApplyLocalization()
    {
        // Static labels are localized in code-behind so language switches apply
        // immediately without a restart. Dynamic text is bound to the ViewModel.
        if (LanguageComboBox != null)
        {
            LanguageComboBox.Header = _localization.GetString("Language");
            LocalizeComboItem(LanguageComboBox, 0, _localization.GetString("LanguageSystem"));
            LocalizeComboItem(LanguageComboBox, 1, _localization.GetString("LanguageEnglish"));
            LocalizeComboItem(LanguageComboBox, 2, _localization.GetString("LanguageChinese"));
        }
        if (ServerConnectionTitle != null) ServerConnectionTitle.Text = _localization.GetString("ServerConnection");
        if (ServerUrlBox != null) ServerUrlBox.Header = _localization.GetString("ServerUrl");
        if (UsernameBox != null) UsernameBox.Header = _localization.GetString("Username");
        if (PasswordBox != null) PasswordBox.Header = _localization.GetString("Password");
        if (ConnectButton != null) ConnectButton.Content = _localization.GetString("Connect");
        if (DisconnectButton != null) DisconnectButton.Content = _localization.GetString("Disconnect");
        if (TestConnectionButton != null) TestConnectionButton.Content = _localization.GetString("TestConnection");
        if (PlaybackTitle != null) PlaybackTitle.Text = _localization.GetString("Playback");
        if (DefaultVolumeLabel != null) DefaultVolumeLabel.Text = _localization.GetString("DefaultVolume");
        if (DefaultSpeedBox != null) DefaultSpeedBox.Header = _localization.GetString("DefaultSpeed");
        if (SkipIntroBox != null) SkipIntroBox.Header = _localization.GetString("SkipIntro");
        if (SkipIntroHint != null) SkipIntroHint.Text = _localization.GetString("SkipIntroHint");
        if (SkipOutroBox != null) SkipOutroBox.Header = _localization.GetString("SkipOutro");
        if (SkipOutroHint != null) SkipOutroHint.Text = _localization.GetString("SkipOutroHint");
        if (DownloadsTitle != null) DownloadsTitle.Text = _localization.GetString("DownloadsSection");
        if (DownloadLocationBox != null) DownloadLocationBox.Header = _localization.GetString("DownloadLocation");
        if (BrowseButton != null) BrowseButton.Content = _localization.GetString("Browse");
        if (AutoDownloadCoversSwitch != null) AutoDownloadCoversSwitch.Header = _localization.GetString("AutoDownloadCovers");
        if (SyncTitle != null) SyncTitle.Text = _localization.GetString("Sync");
        if (AutoSyncSwitch != null) AutoSyncSwitch.Header = _localization.GetString("AutoSyncProgress");
        if (SyncIntervalBox != null) SyncIntervalBox.Header = _localization.GetString("SyncInterval");
        if (AppearanceTitle != null) AppearanceTitle.Text = _localization.GetString("Appearance");
        if (ThemeBox != null) ThemeBox.Header = _localization.GetString("Theme");
        if (StartMinimizedSwitch != null) StartMinimizedSwitch.Header = _localization.GetString("StartMinimized");
        if (MinimizeToTraySwitch != null) MinimizeToTraySwitch.Header = _localization.GetString("MinimizeToTray");
        if (SecurityTitle != null) SecurityTitle.Text = _localization.GetString("Security");
        if (AllowSelfSignedSwitch != null) AllowSelfSignedSwitch.Header = _localization.GetString("AllowSelfSigned");
        if (DataTitle != null) DataTitle.Text = _localization.GetString("Data");
        if (SyncNowButton != null) SyncNowButton.Content = _localization.GetString("SyncNow");
        if (ClearCacheButton != null) ClearCacheButton.Content = _localization.GetString("ClearCache");
        if (DiagnosticsTitle != null) DiagnosticsTitle.Text = _localization.GetString("Diagnostics");
        if (DiagnosticsModeSwitch != null) DiagnosticsModeSwitch.Header = _localization.GetString("DiagnosticsMode");
        if (OpenLogsButton != null) OpenLogsButton.Content = _localization.GetString("OpenLogs");
        if (ExportLogsButton != null) ExportLogsButton.Content = _localization.GetString("ExportLogs");
        if (OpenSettingsFolderButton != null) OpenSettingsFolderButton.Content = _localization.GetString("OpenSettingsFolder");
        if (SettingsDoctorTitle != null) SettingsDoctorTitle.Text = _localization.GetString("SettingsDoctor");
        if (SaveSettingsButton != null) SaveSettingsButton.Content = _localization.GetString("SaveSettings");
        if (ResetConnectionButton != null) ResetConnectionButton.Content = _localization.GetString("ResetConnection");
        if (DoctorTestConnectionButton != null) DoctorTestConnectionButton.Content = _localization.GetString("TestConnection");
        if (AboutTitle != null) AboutTitle.Text = _localization.GetString("About");
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _localization.LanguageChanged -= ApplyLocalization;
    }

    private void ErrorInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.DismissErrorCommand.Execute(null);
    }

    private void SuccessInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.DismissSuccessCommand.Execute(null);
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        var mainWindow = App.Services.GetRequiredService<MainWindow>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            ViewModel.DownloadPath = folder.Path;
        }
    }
}
