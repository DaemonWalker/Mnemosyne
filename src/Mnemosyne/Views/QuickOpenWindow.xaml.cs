using System.Windows;
using System.Windows.Input;
using Mnemosyne.Models;

namespace Mnemosyne.Views;

/// <summary>
/// 快速打开文件弹窗（Ctrl+P）：搜索框 + 文件名/路径两行列表，Enter/双击/确定接受，Esc 关闭。
/// 必须模态：WPF 浮层遮不住 WindowsFormsHost（Scintilla）空域。
/// 数据源由 MainWindow 注入（SetItems）：文件夹扫描结果，或未打开文件夹时的已打开 Tab + 最近文件。
/// </summary>
public partial class QuickOpenWindow : Window
{
    // 单次最多展示的匹配数，避免大文件夹上列表构建卡 UI
    private const int MaxResults = 200;

    private IReadOnlyList<QuickOpenFile> _items = [];

    public QuickOpenWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => FilterBox.Focus();
    }

    public string? SelectedFilePath { get; private set; }

    /// <summary>注入/替换数据源（文件夹扫描完成后会再调一次覆盖兜底列表）</summary>
    public void SetItems(IReadOnlyList<QuickOpenFile> items)
    {
        _items = items;
        ApplyFilter();
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        string filter = FilterBox.Text.Trim();
        IEnumerable<QuickOpenFile> visible;
        if (filter.Length == 0)
        {
            visible = _items;
        }
        else
        {
            // 朴素子串匹配文件名与显示路径，文件名命中的排前面
            visible = _items
                .Where(i => i.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                         || i.DisplayPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(i => i.DisplayPath, StringComparer.OrdinalIgnoreCase);
        }
        FileList.ItemsSource = visible.Take(MaxResults).ToList();
        if (FileList.Items.Count > 0) FileList.SelectedIndex = 0;
    }

    private void Accept()
    {
        if (FileList.SelectedItem is QuickOpenFile item)
        {
            SelectedFilePath = item.FullPath;
            DialogResult = true;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void FileList_KeyDown(object sender, KeyEventArgs e)
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
