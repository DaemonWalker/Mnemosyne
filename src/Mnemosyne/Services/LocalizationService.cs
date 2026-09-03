using System.Windows;

namespace Mnemosyne.Services;

public class LocalizationService
{
    public const string ChineseLanguage = "zh-CN";
    public const string EnglishLanguage = "en";

    private readonly Application _app;

    public LocalizationService(Application app) => _app = app;

    public string CurrentLanguage { get; private set; } = ChineseLanguage;

    public event EventHandler? LanguageChanged;

    public void SetLanguage(string language)
    {
        if (language != EnglishLanguage) language = ChineseLanguage;
        ReplaceDictionary("/i18n/", $"pack://application:,,,/Mnemosyne;component/i18n/Strings.{language}.xaml");
        bool changed = CurrentLanguage != language;
        CurrentLanguage = language;
        if (changed) LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string key) => _app.TryFindResource(key) as string ?? key;

    private void ReplaceDictionary(string pathMarker, string source)
    {
        var dictionaries = _app.Resources.MergedDictionaries;
        ResourceDictionary? existing = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains(pathMarker, StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null) dictionaries.Remove(existing);
        dictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
    }
}
