using System.IO;
using System.Windows;

namespace Mnemosyne.Services;

/// <summary>
/// 主题切换：内置 Dark/Light 走 pack URI 资源字典；插件主题（IThemeContribution 登记）
/// 走 file URI 散装 xaml，并在其下压入 Dark 字典兜底（缺键回退深色主题色）。
/// 主题字典通过字段跟踪替换，UI 全走 DynamicResource 故热切换即刻生效。
/// </summary>
public class ThemeService
{
    public const string DarkThemeName = "Dark";
    public const string LightThemeName = "Light";

    private readonly Application _app;
    private readonly List<ResourceDictionary> _themeDicts = [];
    private readonly Dictionary<string, RegisteredTheme> _pluginThemes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _log;

    public ThemeService(Application app, Action<string>? log = null)
    {
        _app = app;
        _log = log ?? (_ => { });
    }

    public string CurrentTheme { get; private set; } = DarkThemeName;

    /// <summary>可选主题列表：内置 Dark/Light + 已登记插件主题（未扫描时仅内置）。</summary>
    public IReadOnlyList<RegisteredTheme> AvailableThemes =>
        new List<RegisteredTheme>
        {
            new(DarkThemeName, DarkThemeName, ""),
            new(LightThemeName, LightThemeName, ""),
        }
        .Concat(_pluginThemes.Values)
        .ToArray();

    public event EventHandler? ThemeChanged;

    /// <summary>登记插件主题（PluginService 扫描完成后调用；重名后到者已在 PluginService 过滤）</summary>
    public void RegisterPluginThemes(IEnumerable<RegisteredTheme> themes)
    {
        foreach (RegisteredTheme theme in themes)
        {
            _pluginThemes.TryAdd(theme.Name, theme);
        }
    }

    public void ApplyTheme(string themeName)
    {
        List<ResourceDictionary> dicts;
        if (themeName is DarkThemeName or LightThemeName)
        {
            dicts = [PackDictionary(themeName)];
        }
        else if (_pluginThemes.TryGetValue(themeName, out RegisteredTheme? pluginTheme))
        {
            List<ResourceDictionary>? loaded = LoadPluginTheme(pluginTheme);
            if (loaded is null)
            {
                themeName = DarkThemeName;
                dicts = [PackDictionary(DarkThemeName)];
            }
            else
            {
                dicts = loaded;
            }
        }
        else
        {
            themeName = DarkThemeName;
            dicts = [PackDictionary(DarkThemeName)];
        }
        ReplaceDictionaries(dicts);
        bool changed = CurrentTheme != themeName;
        CurrentTheme = themeName;
        if (changed) ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>加载插件主题字典：Dark 垫底 + 插件字典覆盖；文件缺失或解析失败返回 null（调用方回退 Dark）</summary>
    private List<ResourceDictionary>? LoadPluginTheme(RegisteredTheme theme)
    {
        try
        {
            if (!File.Exists(theme.XamlFilePath))
            {
                _log($"主题 {theme.Name} 的资源文件不存在：{theme.XamlFilePath}");
                return null;
            }
            var pluginDict = new ResourceDictionary { Source = new Uri(theme.XamlFilePath, UriKind.Absolute) };
            // Source 是惰性加载，主动触碰强制解析，把解析异常留在本方法内兜底
            _ = pluginDict.Count;
            return [PackDictionary(DarkThemeName), pluginDict];
        }
        catch (Exception ex)
        {
            _log($"加载主题 {theme.Name} 失败：{ex.Message}");
            return null;
        }
    }

    private static ResourceDictionary PackDictionary(string themeName) =>
        new() { Source = new Uri($"pack://application:,,,/Mnemosyne;component/Theming/{themeName}.xaml", UriKind.Absolute) };

    private void ReplaceDictionaries(List<ResourceDictionary> dicts)
    {
        var merged = _app.Resources.MergedDictionaries;
        foreach (ResourceDictionary old in _themeDicts)
        {
            merged.Remove(old);
        }
        _themeDicts.Clear();
        foreach (ResourceDictionary dict in dicts)
        {
            merged.Add(dict);
            _themeDicts.Add(dict);
        }
    }
}
