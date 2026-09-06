using System.Windows.Controls;

namespace Mnemosyne.Views;

/// <summary>Markdown 预览 Tab 内容：ScrollViewer + 渲染结果控件树（纯 WPF，不受 WinForms 空域限制）</summary>
public partial class MarkdownPreviewView : UserControl
{
    public MarkdownPreviewView()
    {
        InitializeComponent();
    }
}
