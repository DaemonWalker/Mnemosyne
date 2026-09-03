using System.Windows;

namespace Mnemosyne.Services;

public class ThemeService
{
    public const string DarkThemeName = "Dark";
    public const string LightThemeName = "Light";

    private readonly Application _app;

    public ThemeService(Application app) => _app = app;

    public string CurrentTheme { get; private set; } = DarkThemeName;

    public event EventHandler? ThemeChanged;

    public void ApplyTheme(string themeName)
    {
        if (themeName != LightThemeName) themeName = DarkThemeName;
        ReplaceDictionary("/Theming/", $"pack://application:,,,/Mnemosyne;component/Theming/{themeName}.xaml");
        bool changed = CurrentTheme != themeName;
        CurrentTheme = themeName;
        if (changed) ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceDictionary(string pathMarker, string source)
    {
        var dictionaries = _app.Resources.MergedDictionaries;
        ResourceDictionary? existing = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains(pathMarker, StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null) dictionaries.Remove(existing);
        dictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
    }
}
