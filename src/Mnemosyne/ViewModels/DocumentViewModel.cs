using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemosyne.Controls;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 一个打开的文档（对应一个 Tab）。持有自己的 ScintillaHost 实例（每 Tab 一个，见 architecture.md 4.2）。
/// 文件 IO 异常直接抛给上层（MainWindowViewModel 统一转为本地化提示）。
/// </summary>
public partial class DocumentViewModel : ObservableObject
{
    private readonly FileService _fileService;
    private readonly LocalizationService _localization;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string _encodingName = "UTF-8";

    [ObservableProperty]
    private string _lineEndingName = "CRLF";

    [ObservableProperty]
    private string _languageName = "Plain Text";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionDisplay))]
    private int _line = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionDisplay))]
    private int _column = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndentDisplay))]
    private bool _indentUseTabs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndentDisplay))]
    private int _indentWidth;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>截断文档（大文件加载被取消后保留的部分内容），保存时禁止静默覆盖原文件</summary>
    [ObservableProperty]
    private bool _isPartialLoad;

    private CancellationTokenSource? _loadCts;

    public DocumentViewModel(FileService fileService, LocalizationService localization, AppSettings settings)
    {
        _fileService = fileService;
        _localization = localization;
        _title = localization.GetString("Loc.Tab.Untitled");

        _indentUseTabs = settings.IndentUseTabs;
        _indentWidth = settings.IndentWidth;
        Editor = new ScintillaHost();
        Editor.ApplyFont(settings.FontFamily, settings.FontSize);
        Editor.SetIndentation(settings.IndentUseTabs, settings.IndentWidth);
        Editor.SetWordWrap(settings.WordWrap);
        Editor.SetViewWhitespace(settings.ShowWhitespace);
        Editor.DirtyChanged += (_, _) =>
        {
            IsDirty = Editor.IsDirty;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        };
        Editor.CaretPositionChanged += (_, _) =>
        {
            Line = Editor.CurrentLineNumber;
            Column = Editor.CurrentColumn;
        };
        _localization.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PositionDisplay));
            OnPropertyChanged(nameof(IndentDisplay));
        };
        SetLanguage(LanguageRegistry.PlainText);
    }

    public ScintillaHost Editor { get; }

    /// <summary>文档内容变化（搜索条借此刷新匹配；保存点变化也会触发，重搜一次无害）</summary>
    public event EventHandler? ContentChanged;

    public Encoding CurrentEncoding { get; private set; } = EncodingCatalog.Utf8NoBom;

    public LanguageDefinition Language { get; private set; } = LanguageRegistry.PlainText;

    public string PositionDisplay => string.Format(_localization.GetString("Loc.Status.LineCol"), Line, Column);

    public string IndentDisplay => IndentUseTabs
        ? string.Format(_localization.GetString("Loc.Status.TabSize"), IndentWidth)
        : string.Format(_localization.GetString("Loc.Status.Spaces"), IndentWidth);

    public async Task LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        FileReadResult result = await _fileService.ReadAsync(path, cancellationToken: cancellationToken);
        ApplyReadResult(path, result);
    }

    /// <summary>
    /// 大文件模式加载：编码探测（头部样本）→ 后台异步分块读 + UI 线程逐块 AppendText（本方法在 UI 线程启动，
    /// await 续体回到 UI 同步上下文，Scintilla 操作线程安全）→ 完成后一次性启用高亮。
    /// 取消时抛出 OperationCanceledException，编辑器中保留已加载部分（由调用方决定去留）。
    /// </summary>
    public async Task LoadLargeFileAsync(
        string path, long fileSize, Encoding? forcedEncoding, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsLoading = true;
        try
        {
            // 先记录目标语言（编辑器内部暂存），BeginChunkedLoad 再把 Lexer 关闭
            SetLanguage(LanguageRegistry.GetForFile(path));
            Encoding encoding = await _fileService.DetectEncodingAsync(path, forcedEncoding, cancellationToken);

            Editor.BeginChunkedLoad();
            bool firstChunk = true;
            try
            {
                await foreach (ReadChunk chunk in _fileService.ReadChunksAsync(path, encoding, _loadCts.Token))
                {
                    Editor.AppendChunk(chunk.Text);
                    if (firstChunk)
                    {
                        firstChunk = false;
                        ApplyContentHints(chunk.Text);
                    }
                    progress.Report((int)Math.Min(100, chunk.BytesRead * 100 / Math.Max(1, fileSize)));
                }
            }
            finally
            {
                Editor.EndChunkedLoad();
                IsDirty = false;
            }
            progress.Report(100);
            FilePath = path;
            Title = Path.GetFileName(path);
            CurrentEncoding = encoding;
            EncodingName = EncodingCatalog.DisplayName(encoding);
            Line = 1;
            Column = 1;
            IsPartialLoad = false;
        }
        finally
        {
            IsLoading = false;
            _loadCts?.Dispose();
            _loadCts = null;
        }
    }

    /// <summary>取消进行中的大文件加载（无加载时为空操作）</summary>
    public void CancelLoad() => _loadCts?.Cancel();

    /// <summary>取消后保留已加载部分：标记为截断文档，保存时禁止直接覆盖原文件（由上层拦截）</summary>
    public void KeepPartialLoad()
    {
        IsPartialLoad = true;
        IsDirty = false;
    }

    /// <summary>按首块内容检测行尾符与缩进风格（样本不足时保留设置项默认）</summary>
    private void ApplyContentHints(string sample)
    {
        LineEnding ending = FileService.DetectLineEnding(sample);
        Editor.SetLineEnding(ending, convert: false);
        LineEndingName = ToDisplayName(ending);
        if (IndentDetector.Detect(sample, IndentWidth) is { } detected)
        {
            SetIndentation(detected.UseTabs, detected.Width);
        }
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        await _fileService.WriteAsync(path, Editor.Text, CurrentEncoding, cancellationToken);
        FilePath = path;
        Title = Path.GetFileName(path);
        Editor.MarkSaved();
        IsDirty = false;
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>文档成功保存到磁盘（Markdown 预览 Tab 借此自动刷新）</summary>
    public event EventHandler? Saved;

    /// <summary>按指定编码重新从磁盘加载（内容被替换，未保存修改丢失，调用方需先确认）</summary>
    public async Task ReloadWithEncodingAsync(Encoding encoding, CancellationToken cancellationToken = default)
    {
        if (FilePath is null) return;
        FileReadResult result = await _fileService.ReadAsync(FilePath, encoding, cancellationToken);
        ApplyReadResult(FilePath, result);
    }

    public void SetLanguage(LanguageDefinition language)
    {
        Language = language;
        LanguageName = language.DisplayName;
        Editor.SetLanguage(language);
    }

    /// <summary>切换行尾符并实际转换文档内容</summary>
    public void ConvertLineEnding(LineEnding ending)
    {
        Editor.SetLineEnding(ending, convert: true);
        LineEndingName = ToDisplayName(ending);
        IsDirty = Editor.IsDirty;
    }

    /// <summary>设置缩进方式与宽度（状态栏点击切换入口；仅影响当前文档的后续输入）</summary>
    public void SetIndentation(bool useTabs, int width)
    {
        IndentUseTabs = useTabs;
        IndentWidth = width;
        Editor.SetIndentation(useTabs, width);
    }

    /// <summary>
    /// 用格式化结果替换全文：插件约定返回 "\n" 行尾符，这里按文档当前行尾符转换；
    /// 替换包在单个撤销动作中（ScintillaHost.ReplaceAllText）。
    /// </summary>
    public void ReplaceAllText(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (Editor.CurrentLineEnding == LineEnding.CrLf)
        {
            normalized = normalized.Replace("\n", "\r\n");
        }
        Editor.ReplaceAllText(normalized);
        IsDirty = Editor.IsDirty;
    }

    /// <summary>跳转到指定行并选中行内字符区间（文件夹搜索结果跳转用）</summary>
    public void GoToMatch(int line, int startInLine, int length)
    {
        Editor.SelectRangeInLine(line, startInLine, length);
        // 程序化 SetSelection 不一定触发 UpdateUI 事件，状态栏行列号手动同步
        Line = Editor.CurrentLineNumber;
        Column = Editor.CurrentColumn;
    }

    private void ApplyReadResult(string path, FileReadResult result)
    {
        FilePath = path;
        Title = Path.GetFileName(path);
        Editor.SetLineEnding(result.LineEnding, convert: false);
        Editor.Text = result.Text;
        CurrentEncoding = result.Encoding;
        EncodingName = EncodingCatalog.DisplayName(result.Encoding);
        LineEndingName = ToDisplayName(result.LineEnding);
        SetLanguage(LanguageRegistry.GetForFile(path));
        // 按内容自动检测缩进风格；样本不足时保留设置项默认（构造函数已应用）
        if (IndentDetector.Detect(result.Text, IndentWidth) is { } detected)
        {
            SetIndentation(detected.UseTabs, detected.Width);
        }
        Line = 1;
        Column = 1;
        IsDirty = false;
    }

    private static string ToDisplayName(LineEnding ending) => ending switch
    {
        LineEnding.Lf => "LF",
        LineEnding.Cr => "CR",
        _ => "CRLF",
    };
}
