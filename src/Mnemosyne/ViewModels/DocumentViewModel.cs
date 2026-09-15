using System.IO;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemosyne.Controls;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// 一个打开的文档（对应一个 Tab）。持有自己的 ScintillaHost 实例（每 Tab 一个，见 architecture.md 4.2）。
/// 文件 IO 异常直接抛给上层（MainWindowViewModel 统一转为本地化提示）。
/// 脏文档内容由本类自行防抖暂存到热退出缓存（进程退出时由上层 FlushStash 兜底）。
/// </summary>
public partial class DocumentViewModel : ObservableObject
{
    private readonly FileService _fileService;
    private readonly LocalizationService _localization;
    private readonly SessionService _sessionService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title;

    // 相对工作区根目录的显示路径（由 MainWindowViewModel 按当前打开文件夹维护；无路径文档为 null）
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string? _relativePath;

    // 存在同名 Tab 时由 MainWindowViewModel 置位，DisplayTitle 改用显示路径以区分
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private bool _showPathInTitle;

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

    private DispatcherTimer? _stashDebounce;
    private FileSystemWatcher? _externalWatcher;
    private DateTime? _lastWriteUtc;
    private bool _suppressExternalEvent;

    public DocumentViewModel(FileService fileService, LocalizationService localization, AppSettings settings, SessionService sessionService)
    {
        _fileService = fileService;
        _localization = localization;
        _sessionService = sessionService;
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
            ScheduleStash();
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

    /// <summary>热退出暂存键：文件文档为路径哈希，新建文档为随机 GUID（首次保存时迁移）</summary>
    public string HotExitKey { get; internal set; } = SessionService.NewKeyForUntitled();

    /// <summary>是否参与热退出暂存（Markdown 预览 Tab 不参与）</summary>
    protected virtual bool ParticipatesInHotExit => true;

    /// <summary>文档内容变化（搜索条借此刷新匹配；保存点变化也会触发，重搜一次无害）</summary>
    public event EventHandler? ContentChanged;

    /// <summary>磁盘上的文件被外部修改/删除（经 mtime 过滤掉本程序自己的写入与噪声事件）</summary>
    public event EventHandler? ExternalChangeDetected;

    public Encoding CurrentEncoding { get; private set; } = EncodingCatalog.Utf8NoBom;

    public LanguageDefinition Language { get; private set; } = LanguageRegistry.PlainText;

    /// <summary>Tab 标签与窗口标题显示用：默认文件名；存在同名 Tab 时改用显示路径（工作区内为相对路径，区外为完整路径），无路径文档始终为 Title</summary>
    public string DisplayTitle => ShowPathInTitle && RelativePath is not null ? RelativePath : Title;

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
        // 自己的写入也会触发 FileSystemWatcher，置抑制标记让首个事件被消费掉
        _suppressExternalEvent = true;
        await _fileService.WriteAsync(path, Editor.Text, CurrentEncoding, cancellationToken);
        FilePath = path;
        Title = Path.GetFileName(path);
        RefreshExternalTimestamp();
        Editor.MarkSaved();
        IsDirty = false;
        // 正常保存后清除对应热退出暂存（FilePath 已设置，暂存键已迁移为路径哈希）
        _sessionService.ClearStash(HotExitKey);
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

    // ===== 热退出暂存 =====

    private void ScheduleStash()
    {
        if (!ParticipatesInHotExit) return;
        if (!IsDirty)
        {
            // 干净态不清暂存：打开/重载文档时也会经过干净态，若此时清除会把待恢复的暂存误删。
            // 暂存的清除只在三处显式发生：保存成功（SaveAsync）、Tab 被移除（上层）、另存为迁移暂存键
            _stashDebounce?.Stop();
            return;
        }
        if (_stashDebounce is null)
        {
            _stashDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _stashDebounce.Tick += OnStashDebounceTick;
        }
        _stashDebounce.Stop();
        _stashDebounce.Start();
    }

    private void OnStashDebounceTick(object? sender, EventArgs e)
    {
        _stashDebounce?.Stop();
        if (!IsDirty) return;
        // 大文档写出放后台线程，避免阻塞 UI
        string content = Editor.Text;
        HotExitStash metadata = CreateStashMetadata();
        string key = HotExitKey;
        _ = Task.Run(() => _sessionService.WriteStash(key, metadata, content));
    }

    /// <summary>进程退出前兜底：同步把脏文档写入暂存（防抖计时器可能还挂着）</summary>
    public void FlushStash()
    {
        if (!ParticipatesInHotExit) return;
        _stashDebounce?.Stop();
        if (IsDirty) _sessionService.WriteStash(HotExitKey, CreateStashMetadata(), Editor.Text);
    }

    /// <summary>Tab 被移除时调用：停防抖、释放外部修改监听。暂存清理由上层按语义决定。</summary>
    public void DisposeResources()
    {
        _stashDebounce?.Stop();
        _externalWatcher?.Dispose();
        _externalWatcher = null;
    }

    private HotExitStash CreateStashMetadata() => new() { FilePath = FilePath, Title = Title };

    /// <summary>用热退出暂存内容替换全文并保持脏状态，同时恢复光标与行尾模式</summary>
    public void RestoreStashContent(string text, int caretPosition)
    {
        Editor.SetTextAsModified(text);
        Editor.SetLineEnding(FileService.DetectLineEnding(text), convert: false);
        LineEndingName = ToDisplayName(Editor.CurrentLineEnding);
        IsDirty = Editor.IsDirty;
        RestoreCaret(caretPosition);
    }

    /// <summary>恢复光标位置（字符索引），并同步状态栏行列号</summary>
    public void RestoreCaret(int position)
    {
        Editor.SetCaret(position);
        Line = Editor.CurrentLineNumber;
        Column = Editor.CurrentColumn;
    }

    // ===== 外部修改检测（FileSystemWatcher + mtime 过滤） =====

    partial void OnFilePathChanged(string? value)
    {
        // 暂存键跟随路径：新建文档首次保存（或另存为）后改用路径哈希键，旧 GUID 暂存清除
        if (value is not null)
        {
            string newKey = SessionService.KeyForPath(value);
            if (!string.Equals(newKey, HotExitKey, StringComparison.Ordinal))
            {
                string oldKey = HotExitKey;
                HotExitKey = newKey;
                _sessionService.ClearStash(oldKey);
            }
        }
        SetupExternalWatcher();
    }

    private void SetupExternalWatcher()
    {
        _externalWatcher?.Dispose();
        _externalWatcher = null;
        _lastWriteUtc = null;
        string? path = FilePath;
        if (path is null) return;
        try
        {
            _lastWriteUtc = File.GetLastWriteTimeUtc(path);
            string? directory = Path.GetDirectoryName(path);
            if (directory is null) return;
            var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += OnExternalFileEvent;
            watcher.Deleted += OnExternalFileEvent;
            watcher.Renamed += OnExternalFileEvent;
            watcher.Error += OnExternalFileEvent;
            watcher.EnableRaisingEvents = true;
            _externalWatcher = watcher;
        }
        catch (Exception)
        {
            // 监听失败仅失去该文档的外部修改检测，不影响编辑本身
        }
    }

    private void OnExternalFileEvent(object? sender, FileSystemEventArgs e) => NotifyExternalEvent();

    private void OnExternalFileEvent(object? sender, RenamedEventArgs e) => NotifyExternalEvent();

    private void OnExternalFileEvent(object? sender, ErrorEventArgs e) => NotifyExternalEvent();

    private void NotifyExternalEvent()
    {
        if (_suppressExternalEvent)
        {
            _suppressExternalEvent = false;
            RefreshExternalTimestamp();
            return;
        }
        // mtime 未真正变化视为噪声（属性/访问时间等），文件消失（删除/改名）总是上报
        string? path = FilePath;
        if (path is not null && File.Exists(path) && _lastWriteUtc is { } last && File.GetLastWriteTimeUtc(path) <= last)
        {
            return;
        }
        Dispatcher dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(() => ExternalChangeDetected?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>把磁盘当前 mtime 记为已见（保存后、用户选择"保留"后调用，避免重复提示）</summary>
    public void RefreshExternalTimestamp()
    {
        string? path = FilePath;
        _lastWriteUtc = path is not null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    /// <summary>用户选择"保留"后清除记录，使下一次外部变化重新提示</summary>
    public void ResetExternalTimestamp() => _lastWriteUtc = null;

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
