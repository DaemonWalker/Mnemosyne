using System.Windows;
using System.Windows.Input;
using Mnemosyne.Controls;
using Mnemosyne.Models;

namespace Mnemosyne.Views;

/// <summary>
/// 语言选择弹窗：列出 Plain Text + 内置语言表 + Lexilla 全部 Lexer，搜索框过滤。
/// 纯 UI 逻辑（列表构建/过滤）放 code-behind。
/// </summary>
public partial class LanguagePickerWindow : Window
{
    private sealed record LanguageItem(string DisplayName, LanguageDefinition Definition);

    private readonly IReadOnlyList<LanguageItem> _items;

    public LanguagePickerWindow()
    {
        InitializeComponent();
        _items = BuildItems();
        LanguageList.ItemsSource = _items;
        if (_items.Count > 0) LanguageList.SelectedIndex = 0;
        Loaded += (_, _) => FilterBox.Focus();
    }

    public LanguageDefinition? SelectedLanguage { get; private set; }

    private static IReadOnlyList<LanguageItem> BuildItems()
    {
        var items = new List<LanguageItem> { new(LanguageRegistry.PlainText.DisplayName, LanguageRegistry.PlainText) };
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LanguageDefinition lang in LanguageRegistry.All)
        {
            items.Add(new LanguageItem(lang.DisplayName, lang));
            covered.Add(lang.LexerName);
        }
        foreach (string lexerName in ScintillaHost.GetAvailableLexerNames())
        {
            if (lexerName == "null" || covered.Contains(lexerName)) continue;
            string display = ToDisplayName(lexerName);
            items.Add(new LanguageItem(display, new LanguageDefinition(display, lexerName, [])));
        }
        return items;
    }

    private static string ToDisplayName(string lexerName)
    {
        return string.IsNullOrEmpty(lexerName)
            ? lexerName
            : char.ToUpperInvariant(lexerName[0]) + lexerName[1..];
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string filter = FilterBox.Text.Trim();
        IEnumerable<LanguageItem> visible = string.IsNullOrEmpty(filter)
            ? _items
            : _items.Where(i => i.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        LanguageList.ItemsSource = visible.ToList();
        if (LanguageList.Items.Count > 0) LanguageList.SelectedIndex = 0;
    }

    private void Accept()
    {
        if (LanguageList.SelectedItem is LanguageItem item)
        {
            SelectedLanguage = item.Definition;
            DialogResult = true;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void LanguageList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void LanguageList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
