using System.Globalization;
using System.Windows.Markup;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 设置窗口 ViewModel。编辑只改本地属性，点"保存设定"才经 MainWindowViewModel.ApplyAllSettings
/// 一次性应用并落盘；取消/Esc 直接关窗，修改从未生效故无需还原。
/// 自动换行/空白显示例外：与视图菜单共用主 VM 属性，保持即时生效。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    // ToString 返回显示文本：ComboBox 折叠态选择框不走 DisplayMemberPath，回退按 ToString 渲染
    public record NamedOption(string Key, string Display)
    {
        public override string ToString() => Display;
    }

    public record FontOption(string Display, string FamilyName, bool IsMonospace)
    {
        public override string ToString() => Display;
    }

    public record IndentModeOption(bool UseTabs, string Display)
    {
        public override string ToString() => Display;
    }

    private readonly ConfigService _configService;
    private readonly LocalizationService _localization;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly ThemeService _themeService;

    public SettingsViewModel(ConfigService configService, LocalizationService localization, MainWindowViewModel mainViewModel, PluginService pluginService, ThemeService themeService)
    {
        _configService = configService;
        _localization = localization;
        _mainViewModel = mainViewModel;
        _themeService = themeService;
        AppSettings settings = configService.Settings;

        PluginGroups = pluginService.Plugins
            .Where(p => p.Settings.Count > 0)
            .Select(p => new PluginSettingsGroupViewModel(p, p.Settings
                .Select(d => new PluginSettingItemViewModel(d, configService.GetPluginSetting(p.Id, d.Key, d.DefaultValue)))
                .ToList()))
            .ToList();

        FontFamilies = Fonts.SystemFontFamilies
            .Select(f => new FontOption(GetFontDisplayName(f), f.Source, IsMonospaceFont(f)))
            .OrderBy(f => f.Display, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        UiFontFamilies = new[] { new FontOption(_localization.GetString("Loc.Settings.UiFontDefault"), "", false) }
            .Concat(FontFamilies)
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
        _hideDotFiles = settings.HideDotFiles;
        _hideHiddenFiles = settings.HideHiddenFiles;
        _selectedUiFontFamily = settings.UiFontFamily;
        _uiFontSize = settings.UiFontSize;
        _allowMultipleInstances = settings.AllowMultipleInstances;
        _shellFileContextMenu = settings.ShellFileContextMenu;
        _shellFolderContextMenu = settings.ShellFolderContextMenu;
        _selectedTerminalShell = settings.TerminalShell;
    }

    public IReadOnlyList<FontOption> FontFamilies { get; }

    /// <summary>插件设置分组（仅含声明了设置项的插件；插件扫描是异步的，取决于打开设置窗口的时机）</summary>
    public IReadOnlyList<PluginSettingsGroupViewModel> PluginGroups { get; }

    /// <summary>是否有插件设置项（控制"插件"分区的可见性）</summary>
    public bool HasPluginSettings => PluginGroups.Count > 0;

    public IReadOnlyList<FontOption> UiFontFamilies { get; }

    public IReadOnlyList<int> IndentWidths { get; }

    /// <summary>保存/取消时请求关窗，参数即 DialogResult</summary>
    public Action<bool>? CloseRequested { get; set; }

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

    [ObservableProperty]
    private bool _hideDotFiles;

    [ObservableProperty]
    private bool _hideHiddenFiles;

    [ObservableProperty]
    private string? _selectedUiFontFamily;

    [ObservableProperty]
    private double _uiFontSize;

    [ObservableProperty]
    private bool _allowMultipleInstances;

    [ObservableProperty]
    private bool _shellFileContextMenu;

    [ObservableProperty]
    private bool _shellFolderContextMenu;

    [ObservableProperty]
    private string? _selectedTerminalShell;

    /// <summary>默认终端 shell 候选（显示名即命令名，无需本地化）</summary>
    public IReadOnlyList<string> TerminalShells { get; } = ["powershell", "pwsh", "cmd"];

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

    [RelayCommand]
    private void Save()
    {
        AppSettings snapshot = _configService.Settings.Clone();
        snapshot.FontFamily = SelectedFontFamily ?? snapshot.FontFamily;
        snapshot.FontSize = Math.Clamp(FontSize, 6, 72);
        snapshot.Theme = SelectedTheme ?? snapshot.Theme;
        snapshot.Language = SelectedLanguage ?? snapshot.Language;
        snapshot.IndentUseTabs = IndentUseTabs;
        snapshot.IndentWidth = IndentWidth;
        snapshot.LargeFileThresholdMB = Math.Clamp(LargeFileThresholdMB, 1, 4096);
        snapshot.HideDotFiles = HideDotFiles;
        snapshot.HideHiddenFiles = HideHiddenFiles;
        snapshot.UiFontFamily = SelectedUiFontFamily ?? "";
        snapshot.UiFontSize = Math.Clamp(UiFontSize, 8, 32);
        snapshot.AllowMultipleInstances = AllowMultipleInstances;
        snapshot.ShellFileContextMenu = ShellFileContextMenu;
        snapshot.ShellFolderContextMenu = ShellFolderContextMenu;
        snapshot.TerminalShell = SelectedTerminalShell ?? snapshot.TerminalShell;
        ApplyPluginSettings(snapshot);
        _mainViewModel.ApplyAllSettings(snapshot);
        CloseRequested?.Invoke(true);
    }

    /// <summary>把插件分区的编辑值写入设置快照的 PluginSettings 节（键值由各插件 descriptor 声明）</summary>
    private void ApplyPluginSettings(AppSettings snapshot)
    {
        foreach (PluginSettingsGroupViewModel group in PluginGroups)
        {
            if (!snapshot.PluginSettings.TryGetValue(group.PluginId, out Dictionary<string, System.Text.Json.JsonElement>? values))
            {
                values = new Dictionary<string, System.Text.Json.JsonElement>();
                snapshot.PluginSettings[group.PluginId] = values;
            }
            foreach (PluginSettingItemViewModel item in group.Items)
            {
                values[item.Descriptor.Key] = System.Text.Json.JsonSerializer.SerializeToElement(item.Value);
            }
        }
    }

    private void RebuildOptions()
    {
        // 内置主题沿用本地化显示名；插件主题用其声明的 DisplayName（插件扫描异步，列表反映开窗那一刻）
        Themes = _themeService.AvailableThemes
            .Select(t => new NamedOption(t.Name, t.Name switch
            {
                ThemeService.DarkThemeName => _localization.GetString("Loc.Settings.Theme.Dark"),
                ThemeService.LightThemeName => _localization.GetString("Loc.Settings.Theme.Light"),
                _ => t.DisplayName,
            }))
            .ToArray();
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

    /// <summary>比较 i/l 与 W/M 的字形步宽判定等宽；取不到字形信息时按非等宽处理</summary>
    private static bool IsMonospaceFont(System.Windows.Media.FontFamily family)
    {
        try
        {
            foreach (Typeface typeface in family.GetTypefaces())
            {
                if (!typeface.TryGetGlyphTypeface(out GlyphTypeface glyph)) continue;
                var map = glyph.CharacterToGlyphMap;
                if (!map.TryGetValue('i', out ushort i) || !map.TryGetValue('W', out ushort w)) continue;
                if (!map.TryGetValue('l', out ushort l)) l = i;
                if (!map.TryGetValue('M', out ushort m)) m = w;
                // AdvanceWidths 已按 DesignEmHeight 归一化，直接比较
                double width = glyph.AdvanceWidths[i];
                return width == glyph.AdvanceWidths[l]
                    && width == glyph.AdvanceWidths[w]
                    && width == glyph.AdvanceWidths[m];
            }
        }
        catch (Exception)
        {
        }
        return false;
    }
}
