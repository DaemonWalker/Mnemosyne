using System.Windows;
using System.Windows.Controls;
using Mnemosyne.Services;

namespace Mnemosyne.Views;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localization;
    private bool _ready;

    public MainWindow(ConfigService configService, ThemeService themeService, LocalizationService localization)
    {
        InitializeComponent();
        _configService = configService;
        _themeService = themeService;
        _localization = localization;

        PopulateDemoCombos();
        _localization.LanguageChanged += OnLanguageChanged;
        _ready = true;
        RefreshPendingPathsText();
    }

    private void PopulateDemoCombos()
    {
        _ready = false;
        ThemeComboBox.ItemsSource = new[]
        {
            _localization.GetString("Loc.Demo.Theme.Dark"),
            _localization.GetString("Loc.Demo.Theme.Light"),
        };
        ThemeComboBox.SelectedIndex = _themeService.CurrentTheme == ThemeService.LightThemeName ? 1 : 0;
        LanguageComboBox.ItemsSource = new[]
        {
            _localization.GetString("Loc.Demo.Language.zhCN"),
            _localization.GetString("Loc.Demo.Language.en"),
        };
        LanguageComboBox.SelectedIndex = _localization.CurrentLanguage == LocalizationService.EnglishLanguage ? 1 : 0;
        _ready = true;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        PopulateDemoCombos();
        RefreshPendingPathsText();
    }

    private void RefreshPendingPathsText()
    {
        var paths = ((App)Application.Current).PendingOpenPaths;
        PendingPathsText.Text = paths.Count == 0
            ? _localization.GetString("Loc.Demo.NoPendingPaths")
            : string.Join(Environment.NewLine, paths);
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ThemeComboBox.SelectedIndex < 0) return;
        string theme = ThemeComboBox.SelectedIndex == 1 ? ThemeService.LightThemeName : ThemeService.DarkThemeName;
        _themeService.ApplyTheme(theme);
        _configService.Settings.Theme = theme;
        _configService.Save();
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LanguageComboBox.SelectedIndex < 0) return;
        string language = LanguageComboBox.SelectedIndex == 1 ? LocalizationService.EnglishLanguage : LocalizationService.ChineseLanguage;
        _localization.SetLanguage(language);
        _configService.Settings.Language = language;
        _configService.Save();
    }
}
