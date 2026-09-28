using System.Windows.Input;

namespace Mnemosyne.Commands;

public static class AppCommands
{
    public static RoutedUICommand OpenFile { get; } = Create(nameof(OpenFile), Key.O);

    // 热退出需要支持"从未保存过的新建文档"，故提供新建文件入口（Ctrl+N 为常见约定）
    public static RoutedUICommand NewFile { get; } = Create(nameof(NewFile), Key.N);

    public static RoutedUICommand OpenFolder { get; } = Create(nameof(OpenFolder), Key.O, ModifierKeys.Shift);

    public static RoutedUICommand QuickOpen { get; } = Create(nameof(QuickOpen), Key.P);

    public static RoutedUICommand Save { get; } = Create(nameof(Save), Key.S);

    public static RoutedUICommand SaveAs { get; } = Create(nameof(SaveAs), Key.S, ModifierKeys.Shift);

    public static RoutedUICommand Find { get; } = Create(nameof(Find), Key.F);

    public static RoutedUICommand Replace { get; } = Create(nameof(Replace), Key.H);

    public static RoutedUICommand SearchInFolder { get; } = Create(nameof(SearchInFolder), Key.F, ModifierKeys.Shift);

    public static RoutedUICommand CloseTab { get; } = Create(nameof(CloseTab), Key.W);

    public static RoutedUICommand SelectNextOccurrence { get; } = Create(nameof(SelectNextOccurrence), Key.D);

    // 无快捷键（曾用 Ctrl+Shift+V，已让给纯文本粘贴）：仅视图菜单入口
    public static RoutedUICommand OpenMarkdownPreview { get; } =
        new(nameof(OpenMarkdownPreview), nameof(OpenMarkdownPreview), typeof(AppCommands));

    // 无快捷键：Markdown 文档中普通 Ctrl+V 已默认转换粘贴（AutoConvertHtmlPaste），
    // 此命令保留菜单入口，供非 Markdown 文档显式使用
    public static RoutedUICommand PasteAsMarkdown { get; } =
        new(nameof(PasteAsMarkdown), nameof(PasteAsMarkdown), typeof(AppCommands));

    // Ctrl+Shift+V：强制纯文本粘贴（绕过 HTML→Markdown 自动转换）
    public static RoutedUICommand PastePlainText { get; } = Create(nameof(PastePlainText), Key.V, ModifierKeys.Shift);

    // 无快捷键：仅菜单/编辑器右键入口（需求 4.11 未为其定义快捷键）
    public static RoutedUICommand FormatDocument { get; } =
        new(nameof(FormatDocument), nameof(FormatDocument), typeof(AppCommands));

    public static RoutedUICommand OpenSettings { get; } = Create(nameof(OpenSettings), Key.OemComma);

    // 终端聚焦时控件自身不吞这两个键，保证窗口级绑定照常触发切换
    // VSCode 同款：Ctrl+` 切换终端，Ctrl+J 切换面板
    public static RoutedUICommand ToggleTerminal { get; } = new(nameof(ToggleTerminal), nameof(ToggleTerminal), typeof(AppCommands),
        new InputGestureCollection
        {
            new KeyGesture(Key.OemTilde, ModifierKeys.Control),
            new KeyGesture(Key.J, ModifierKeys.Control),
        });

    // Ctrl+Shift+` 新建终端 tab（VSCode 同款）；OemTilde 同样不被终端控件吞掉
    public static RoutedUICommand NewTerminalTab { get; } = Create(nameof(NewTerminalTab), Key.OemTilde, ModifierKeys.Shift);

    // 手势直接挂在命令上：菜单自动显示快捷键文本，窗口注册 CommandBinding 后即全局生效
    private static RoutedUICommand Create(string name, Key key, ModifierKeys extraModifiers = ModifierKeys.None)
    {
        return new RoutedUICommand(name, name, typeof(AppCommands),
            new InputGestureCollection { new KeyGesture(key, ModifierKeys.Control | extraModifiers) });
    }
}
