using System.Globalization;
using System.Windows.Markup;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 设置窗口 ViewModel。所有改动即时应用（经 MainWindowViewModel 的应用方法遍历已打开文档并落盘），
/// 窗口本身不持有"确定/取消"语义。自动换行/空白显示直接转发到主 VM（与视图菜单共用同一属性，双向同步）。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    public record NamedOption(string Key, string Display);

    public record FontOption(string Display, string FamilyName);

    public record IndentModeOption(bool UseTabs, string Display);

    private readonly ConfigService _configService;
    private readonly LocalizationService _localization;
    private readonly MainWindowViewModel _mainViewModel;
    private bool _initializing = true;

    public SettingsViewModel(ConfigService configService, LocalizationService localization, MainWindowViewModel mainViewModel)
    {
        _configService = configService;
        _localization = localization;
        _mainViewModel = mainViewModel;
        AppSettings settings = configService.Settings;

        FontFamilies = Fonts.SystemFontFamilies
            .Select(f => new FontOption(GetFontDisplayName(f), f.Source))
            .OrderBy(f => f.Display, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        IndentWidths = [2, 3, 4, 8];
        RebuildOptions();

        _selectedFontFamily = settings.FontFamily;
        _fontSize = settings.FontSize;
        _selectedTheme = settings.Theme;
        _selectedLanguage = settings.Language;
        _indentUseTabs = settings.IndentUseTabs;
        _indentWidth = settings.IndentWidth;
        _largeFileThresholdMB = settings.LargeFileThresholdMB;
        _initializing = false;

        // 语言切换后选项显示名需要按新语言重建
        _localization.LanguageChanged += (_, _) =>
        {
            _initializing = true;
            RebuildOptions();
            OnPropertyChanged(nameof(SelectedTheme));
            OnPropertyChanged(nameof(SelectedLanguage));
            OnPropertyChanged(nameof(IndentUseTabs));
            _initializing = false;
        };
        // 视图菜单切换换行/空白时同步本窗口勾选状态
        _mainViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.WordWrap)) OnPropertyChanged(nameof(WordWrap));
            if (e.PropertyName == nameof(MainWindowViewModel.ShowWhitespace)) OnPropertyChanged(nameof(ShowWhitespace));
        };
    }

    public IReadOnlyList<FontOption> FontFamilies { get; }

    public IReadOnlyList<int> IndentWidths { get; }

    [ObservableProperty]
    private IReadOnlyList<NamedOption> _themes = [];

    [ObservableProperty]
    private IReadOnlyList<NamedOption> _languages = [];

    [ObservableProperty]
    private IReadOnlyList<IndentModeOption> _indentModes = [];

    [ObservableProperty]
    private string? _selectedFontFamily;

    [ObservableProperty]
    private double _fontSize;

    [ObservableProperty]
    private string? _selectedTheme;

    [ObservableProperty]
    private string? _selectedLanguage;

    [ObservableProperty]
    private bool _indentUseTabs;

    [ObservableProperty]
    private int _indentWidth;

    [ObservableProperty]
    private int _largeFileThresholdMB;

    /// <summary>自动换行：与主 VM（视图菜单）共用同一属性</summary>
    public bool WordWrap
    {
        get => _mainViewModel.WordWrap;
        set => _mainViewModel.WordWrap = value;
    }

    /// <summary>空白字符显示：与主 VM（视图菜单）共用同一属性</summary>
    public bool ShowWhitespace
    {
        get => _mainViewModel.ShowWhitespace;
        set => _mainViewModel.ShowWhitespace = value;
    }

    partial void OnSelectedFontFamilyChanged(string? value)
    {
        if (_initializing || string.IsNullOrEmpty(value)) return;
        _mainViewModel.ApplyFontSettings(value, FontSize);
    }

    partial void OnFontSizeChanged(double value)
    {
        if (_initializing) return;
        double clamped = Math.Clamp(value, 6, 72);
        _mainViewModel.ApplyFontSettings(SelectedFontFamily ?? _configService.Settings.FontFamily, clamped);
    }

    partial void OnSelectedThemeChanged(string? value)
    {
        if (_initializing || value is null) return;
        _mainViewModel.ApplyThemeSetting(value);
    }

    partial void OnSelectedLanguageChanged(string? value)
    {
        if (_initializing || value is null) return;
        _mainViewModel.ApplyLanguageSetting(value);
    }

    partial void OnIndentUseTabsChanged(bool value)
    {
        if (_initializing) return;
        _mainViewModel.ApplyIndentSettings(value, IndentWidth);
    }

    partial void OnIndentWidthChanged(int value)
    {
        if (_initializing) return;
        _mainViewModel.ApplyIndentSettings(IndentUseTabs, value);
    }

    partial void OnLargeFileThresholdMBChanged(int value)
    {
        if (_initializing) return;
        _mainViewModel.ApplyLargeFileThreshold(Math.Clamp(value, 1, 4096));
    }

    private void RebuildOptions()
    {
        Themes =
        [
            new NamedOption(ThemeService.DarkThemeName, _localization.GetString("Loc.Settings.Theme.Dark")),
            new NamedOption(ThemeService.LightThemeName, _localization.GetString("Loc.Settings.Theme.Light")),
        ];
        Languages =
        [
            new NamedOption(LocalizationService.ChineseLanguage, _localization.GetString("Loc.Settings.Language.Chinese")),
            new NamedOption(LocalizationService.EnglishLanguage, _localization.GetString("Loc.Settings.Language.English")),
        ];
        IndentModes =
        [
            new IndentModeOption(false, _localization.GetString("Loc.Indent.UseSpaces")),
            new IndentModeOption(true, _localization.GetString("Loc.Indent.UseTabs")),
        ];
    }

    private static string GetFontDisplayName(System.Windows.Media.FontFamily family)
    {
        var language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.Name);
        return family.FamilyNames.TryGetValue(language, out string? name) ? name : family.Source;
    }
}
