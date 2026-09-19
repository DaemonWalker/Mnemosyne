using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using XtermSharp;
using Mnemosyne.Services.Terminal;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using XBuffer = XtermSharp.Buffer;
using XColor = XtermSharp.Color;

namespace Mnemosyne.Controls;

/// <summary>
/// 终端自绘渲染控件（纯 WPF，无 HWND 空域问题）。固定字符网格逐行绘制 XtermSharp 缓冲，
/// 颜色全部取自主题资源（Color.Terminal.*），主题切换经 ApplyThemeToAll 重刷。
/// </summary>
public class TerminalControl : FrameworkElement
{
    private const int WheelLinesPerNotch = 3;

    private static readonly List<WeakReference<TerminalControl>> _instances = [];

    private TerminalSession? _session;
    private Typeface _typeface;
    private double _fontSize = 13;
    private double _cellWidth = 8;
    private double _cellHeight = 16;
    private double _pixelsPerDip = 1;

    private Brush _backgroundBrush = Brushes.Black;
    private Brush _foregroundBrush = Brushes.White;
    private Brush _cursorBrush = Brushes.Gray;
    private Brush _selectionBrush = Brushes.DarkSlateBlue;
    private readonly Color[] _palette = new Color[256];
    private readonly Dictionary<int, Brush> _brushCache = [];

    private readonly DispatcherTimer _cursorTimer;
    private bool _cursorPhase = true;
    private bool _selecting;
    // 订阅会话事件时捕获会话本体，重启会话后旧会话残留的已排队回调不会灌进新终端
    private Action<byte[]>? _outputHandler;

    /// <summary>可视尺寸换算出的行列数变化（参数 cols/rows），供会话同步终端尺寸</summary>
    public event Action<int, int>? GridSizeChanged;

    public TerminalControl()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        _typeface = MakeTypeface();
        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorPhase = !_cursorPhase;
            InvalidateVisual();
        };
        lock (_instances) _instances.Add(new WeakReference<TerminalControl>(this));
        IsVisibleChanged += (_, _) => UpdateCursorTimer();
        ApplyTheme();
    }

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(TerminalSession), typeof(TerminalControl),
        new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(TerminalControl),
        new PropertyMetadata(13.0, OnFontSizeChanged));

    public TerminalSession? Session
    {
        get => (TerminalSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TerminalControl)d;
        if (e.OldValue is TerminalSession oldSession)
        {
            oldSession.OutputReceived -= control._outputHandler;
            oldSession.Exited -= control.OnSessionExited;
            oldSession.Terminal.Scrolled -= control.OnTerminalScrolled;
            oldSession.Terminal.Buffers.Activated -= control.OnBuffersActivated;
        }
        control._session = e.NewValue as TerminalSession;
        if (control._session is not null)
        {
            TerminalSession captured = control._session;
            control._outputHandler = data => control.OnSessionOutput(captured, data);
            control._session.OutputReceived += control._outputHandler;
            control._session.Exited += control.OnSessionExited;
            control._session.Terminal.Scrolled += control.OnTerminalScrolled;
            control._session.Terminal.Buffers.Activated += control.OnBuffersActivated;
        }
        control.UpdateCursorTimer();
        control.InvalidateVisual();
    }

    public int Cols => Math.Max(1, (int)(ActualWidth / _cellWidth));

    public int Rows => Math.Max(1, (int)(ActualHeight / _cellHeight));

    /// <summary>终端字号（默认取编辑器字号，由宿主设置）</summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private static void OnFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TerminalControl)d;
        control._fontSize = (double)e.NewValue;
        control.UpdateCellMetrics();
        control.InvalidateVisual();
    }

    // 隐藏（非激活 tab / 面板收起）时暂停光标闪烁计时器，避免空转
    private void UpdateCursorTimer() => _cursorTimer.IsEnabled = _session is not null && IsVisible;

    public static void ApplyThemeToAll()
    {
        lock (_instances)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i].TryGetTarget(out TerminalControl? control))
                {
                    control.ApplyTheme();
                    control.InvalidateVisual();
                }
                else
                {
                    _instances.RemoveAt(i);
                }
            }
        }
    }

    public void ApplyTheme()
    {
        _backgroundBrush = ResourceBrush("Brush.Terminal.Background", Brushes.Black);
        _foregroundBrush = ResourceBrush("Brush.Terminal.Foreground", Brushes.White);
        _cursorBrush = ResourceBrush("Brush.Terminal.Cursor", Brushes.Gray);
        _selectionBrush = ResourceBrush("Brush.Terminal.Selection", Brushes.DarkSlateBlue);
        for (int i = 0; i < 256; i++)
        {
            XColor ansi = XColor.DefaultAnsiColors[i];
            _palette[i] = i < 16 && TryFindResource($"Color.Terminal.Palette{i}") is Color themed
                ? themed
                : Color.FromRgb(ansi.Red, ansi.Green, ansi.Blue);
        }
        _brushCache.Clear();
    }

    private Brush ResourceBrush(string key, Brush fallback) =>
        TryFindResource(key) is Brush brush ? brush : fallback;

    // 粗体作用于 0-7 调色板色时改用亮色变体（经典 xterm 行为）
    private Brush BrushFor(int colorCode, bool isForeground, bool bold)
    {
        if (colorCode == Renderer.DefaultColor)
            return isForeground ? _foregroundBrush : _backgroundBrush;
        if (colorCode == Renderer.InvertedDefaultColor)
            return isForeground ? _backgroundBrush : _foregroundBrush;
        if (bold && isForeground && colorCode < 8) colorCode += 8;
        if (!_brushCache.TryGetValue(colorCode, out Brush? brush))
        {
            brush = new SolidColorBrush(_palette[colorCode]);
            brush.Freeze();
            _brushCache[colorCode] = brush;
        }
        return brush;
    }

    private static Typeface MakeTypeface()
    {
        var family = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
        return new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    private void UpdateCellMetrics()
    {
        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface, _fontSize, Brushes.Black, _pixelsPerDip);
        _cellWidth = Math.Max(1, ft.Width);
        _cellHeight = Math.Max(1, ft.Height);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateCellMetrics();
        if (sizeInfo.NewSize.Width > 0 && sizeInfo.NewSize.Height > 0)
        {
            GridSizeChanged?.Invoke(Cols, Rows);
        }
    }

    private void OnSessionOutput(TerminalSession session, byte[] data)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_session, session)) return;
            session.Feed(data);
            InvalidateVisual();
        });
    }

    private void OnSessionExited(int exitCode)
    {
        // 退出覆盖提示由 TerminalViewModel 订阅会话 Exited 处理，控件只需停止光标闪烁
        Dispatcher.BeginInvoke(() => _cursorTimer.IsEnabled = false);
    }

    private void OnTerminalScrolled(Terminal terminal, int yDisp) => Dispatcher.BeginInvoke(InvalidateVisual);

    private void OnBuffersActivated(XBuffer activated, XBuffer deactivated) => Dispatcher.BeginInvoke(InvalidateVisual);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(_backgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        TerminalSession? session = _session;
        if (session is null) return;

        Terminal terminal = session.Terminal;
        XBuffer buffer = terminal.Buffer;
        CircularList<BufferLine> lines = buffer.Lines;
        int rows = terminal.Rows;
        int cols = terminal.Cols;
        int yDisp = buffer.YDisp;
        SelectionService selection = session.Selection;

        for (int row = 0; row < rows; row++)
        {
            int lineIndex = row + yDisp;
            if (lineIndex >= lines.Length) break;
            RenderLine(dc, lines[lineIndex], lineIndex, row, cols, selection);
        }

        RenderCursor(dc, terminal, buffer, yDisp, rows);
    }

    private void RenderLine(DrawingContext dc, BufferLine line, int lineIndex, int row, int cols, SelectionService selection)
    {
        double y = row * _cellHeight;
        int selFrom = -1, selTo = -1;
        if (selection.Active && selection.Start != selection.End)
        {
            (XtermSharp.Point lo, XtermSharp.Point hi) = ComparePoints(selection.Start, selection.End) <= 0
                ? (selection.Start, selection.End)
                : (selection.End, selection.Start);
            if (lineIndex >= lo.Y && lineIndex <= hi.Y)
            {
                selFrom = lineIndex == lo.Y ? lo.X : 0;
                selTo = lineIndex == hi.Y ? hi.X : cols - 1;
            }
        }

        int col = 0;
        while (col < cols && col < line.Length)
        {
            CharData ch = line[col];
            if (ch.IsNullChar())
            {
                col++;
                continue;
            }

            int attribute = ch.Attribute;
            var run = new StringBuilder();
            int startCol = col;
            while (col < cols && col < line.Length)
            {
                CharData c = line[col];
                if (c.Attribute != attribute || c.IsNullChar()) break;
                run.Append(c.Code > 0 && c.Code <= 0x10FFFF ? char.ConvertFromUtf32(c.Code) : "");
                col += Math.Max(c.Width, 1);
            }

            (Brush fg, Brush bg) = ResolveColors(attribute);
            double x = startCol * _cellWidth;
            double runWidth = (col - startCol) * _cellWidth;
            if (!ReferenceEquals(bg, _backgroundBrush))
            {
                dc.DrawRectangle(bg, null, new Rect(x, y, runWidth, _cellHeight));
            }
            string runText = run.ToString();
            if (runText.Trim().Length > 0)
            {
                var ft = new FormattedText(runText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    _typeface, _fontSize, fg, _pixelsPerDip);
                dc.DrawText(ft, new System.Windows.Point(x, y));
            }
        }

        if (selFrom >= 0)
        {
            dc.DrawRectangle(_selectionBrush, null,
                new Rect(selFrom * _cellWidth, y, (selTo - selFrom + 1) * _cellWidth, _cellHeight));
        }
    }

    private (Brush Foreground, Brush Background) ResolveColors(int attribute)
    {
        int bg = attribute & 0x1ff;
        int fg = (attribute >> 9) & 0x1ff;
        var flags = (FLAGS)(attribute >> 18);
        if (flags.HasFlag(FLAGS.INVERSE)) (fg, bg) = (bg, fg);
        return (BrushFor(fg, true, flags.HasFlag(FLAGS.BOLD)), BrushFor(bg, false, false));
    }

    private void RenderCursor(DrawingContext dc, Terminal terminal, XBuffer buffer, int yDisp, int rows)
    {
        if (!IsFocused || !_cursorPhase || terminal.CursorHidden) return;
        int cursorRow = buffer.YBase + buffer.Y - yDisp;
        if (cursorRow < 0 || cursorRow >= rows) return;
        var rect = new Rect(buffer.X * _cellWidth, cursorRow * _cellHeight, _cellWidth, _cellHeight);
        dc.DrawRectangle(_cursorBrush, null, rect);
    }

    private static int ComparePoints(XtermSharp.Point a, XtermSharp.Point b) =>
        a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);

    // ===== 键盘输入 → VT 序列 =====

    protected override void OnKeyDown(KeyEventArgs e)
    {
        TerminalSession? session = _session;
        if (session is null || !session.IsRunning)
        {
            base.OnKeyDown(e);
            return;
        }

        ModifierKeys mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                // Ctrl+` / Ctrl+J 不吞，冒泡到窗口命令绑定切换面板
                case Key.OemTilde:
                case Key.J:
                    return;
                case Key.C:
                    if (session.Selection.Active && session.Selection.Start != session.Selection.End) CopySelection();
                    else session.SendBytes([0x03]);
                    e.Handled = true;
                    return;
                case Key.V:
                    PasteClipboard();
                    e.Handled = true;
                    return;
            }
            if (e.Key >= Key.A && e.Key <= Key.Z)
            {
                session.SendBytes([(byte)(e.Key - Key.A + 1)]);
                e.Handled = true;
                return;
            }
        }

        Terminal terminal = session.Terminal;
        switch (e.Key)
        {
            case Key.Enter:
                session.SendBytes(EscapeSequences.CmdRet);
                break;
            case Key.Back:
                session.SendBytes([0x7f]);
                break;
            case Key.Tab:
                session.SendBytes(mods.HasFlag(ModifierKeys.Shift) ? EscapeSequences.CmdBackTab : EscapeSequences.CmdTab);
                break;
            case Key.Escape:
                session.SendBytes(EscapeSequences.CmdEsc);
                break;
            case Key.Delete:
                session.SendBytes(EscapeSequences.CmdDelKey);
                break;
            case Key.Up:
                session.SendBytes(terminal.ApplicationCursor ? EscapeSequences.MoveUpApp : EscapeSequences.MoveUpNormal);
                break;
            case Key.Down:
                session.SendBytes(terminal.ApplicationCursor ? EscapeSequences.MoveDownApp : EscapeSequences.MoveDownNormal);
                break;
            case Key.Left:
                session.SendBytes(terminal.ApplicationCursor ? EscapeSequences.MoveLeftApp : EscapeSequences.MoveLeftNormal);
                break;
            case Key.Right:
                session.SendBytes(terminal.ApplicationCursor ? EscapeSequences.MoveRightApp : EscapeSequences.MoveRightNormal);
                break;
            case Key.Home:
                session.SendBytes(terminal.ApplicationCursor ? EscapeSequences.MoveHomeApp : EscapeSequences.MoveHomeNormal);
                break;
            case Key.End:
                session.SendBytes(terminal.ApplicationCursor ? EscapeSequences.MoveEndApp : EscapeSequences.MoveEndNormal);
                break;
            case Key.PageUp:
                if (terminal.ApplicationCursor) session.SendBytes(EscapeSequences.CmdPageUp);
                else terminal.ScrollLines(-terminal.Rows);
                break;
            case Key.PageDown:
                if (terminal.ApplicationCursor) session.SendBytes(EscapeSequences.CmdPageDown);
                else terminal.ScrollLines(terminal.Rows);
                break;
            case >= Key.F1 and <= Key.F12:
                session.SendBytes(EscapeSequences.CmdF[e.Key - Key.F1]);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (_session is null || string.IsNullOrEmpty(e.Text))
        {
            base.OnTextInput(e);
            return;
        }
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            _session.SendText(e.Text);
            e.Handled = true;
            return;
        }
        base.OnTextInput(e);
    }

    // ===== 鼠标：滚屏 / 拖选 / 右键菜单 =====

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_session is not null)
        {
            _session.Terminal.ScrollLines(-e.Delta / 120 * WheelLinesPerNotch);
            e.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
    }

    private (int Row, int Col) CellFromPoint(System.Windows.Point position) =>
        (Math.Clamp((int)(position.Y / _cellHeight), 0, Rows - 1),
         Math.Clamp((int)(position.X / _cellWidth), 0, Cols - 1));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_session is not null)
        {
            CaptureMouse();
            _selecting = true;
            (int row, int col) = CellFromPoint(e.GetPosition(this));
            _session.Selection.StartSelection(row, col);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_selecting && _session is not null)
        {
            (int row, int col) = CellFromPoint(e.GetPosition(this));
            _session.Selection.DragExtend(row, col);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_selecting)
        {
            _selecting = false;
            ReleaseMouseCapture();
            // 单击未拖动时清除选区
            if (_session is not null && _session.Selection.Start == _session.Selection.End)
            {
                _session.Selection.SelectNone();
                _session.Selection.Active = false;
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (_session is not null)
        {
            var menu = new ContextMenu();
            bool hasSelection = _session.Selection.Active && _session.Selection.Start != _session.Selection.End;
            var copyItem = new MenuItem
            {
                Header = TryFindResource("Loc.Terminal.Copy") ?? "Copy",
                IsEnabled = hasSelection,
            };
            copyItem.Click += (_, _) => CopySelection();
            var pasteItem = new MenuItem
            {
                Header = TryFindResource("Loc.Terminal.Paste") ?? "Paste",
                IsEnabled = _session.IsRunning && Clipboard.ContainsText(),
            };
            pasteItem.Click += (_, _) => PasteClipboard();
            menu.Items.Add(copyItem);
            menu.Items.Add(pasteItem);
            menu.PlacementTarget = this;
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }
        base.OnMouseRightButtonUp(e);
    }

    private void CopySelection()
    {
        if (_session is null) return;
        string text = _session.Selection.GetSelectedText();
        if (text.Length == 0) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // 剪贴板被占用时放弃本次复制
        }
    }

    private void PasteClipboard()
    {
        if (_session is null || !_session.IsRunning) return;
        try
        {
            if (Clipboard.ContainsText()) _session.SendText(Clipboard.GetText(), isPaste: true);
        }
        catch (Exception)
        {
            // 剪贴板被占用时放弃本次粘贴
        }
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateCellMetrics();
        return base.MeasureOverride(availableSize);
    }
}
