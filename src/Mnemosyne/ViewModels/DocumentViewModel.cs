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

    public DocumentViewModel(FileService fileService, LocalizationService localization, AppSettings settings)
    {
        _fileService = fileService;
        _localization = localization;
        _title = localization.GetString("Loc.Tab.Untitled");

        Editor = new ScintillaHost();
        Editor.ApplyFont(settings.FontFamily, settings.FontSize);
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
        _localization.LanguageChanged += (_, _) => OnPropertyChanged(nameof(PositionDisplay));
        SetLanguage(LanguageRegistry.PlainText);
    }

    public ScintillaHost Editor { get; }

    /// <summary>文档内容变化（搜索条借此刷新匹配；保存点变化也会触发，重搜一次无害）</summary>
    public event EventHandler? ContentChanged;

    public Encoding CurrentEncoding { get; private set; } = EncodingCatalog.Utf8NoBom;

    public LanguageDefinition Language { get; private set; } = LanguageRegistry.PlainText;

    public string PositionDisplay => string.Format(_localization.GetString("Loc.Status.LineCol"), Line, Column);

    public async Task LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        FileReadResult result = await _fileService.ReadAsync(path, cancellationToken: cancellationToken);
        ApplyReadResult(path, result);
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        await _fileService.WriteAsync(path, Editor.Text, CurrentEncoding, cancellationToken);
        FilePath = path;
        Title = Path.GetFileName(path);
        Editor.MarkSaved();
        IsDirty = false;
    }

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
