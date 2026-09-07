using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemosyne.Models;
using Mnemosyne.Services;

namespace Mnemosyne.ViewModels;

/// <summary>
/// Markdown 预览 Tab（纯 WPF 内容，无编辑器）。继承 DocumentViewModel 以复用 Tab 系统（标题/关闭/拖拽排序）。
/// 源文档保存后自动刷新（内容无变化跳过重建，避免闪烁）；源 Tab 关闭由 MainWindowViewModel 联动关闭本 Tab。
/// </summary>
public partial class MarkdownPreviewViewModel : DocumentViewModel
{
    private readonly MarkdownRenderService _renderService;
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly Action<string> _openLocalFile;
    private readonly Action<string, string>? _showError;
    private string? _renderedText;

    public MarkdownPreviewViewModel(
        DocumentViewModel source,
        MarkdownRenderService renderService,
        FileService fileService,
        LocalizationService localization,
        AppSettings settings,
        SessionService sessionService,
        Action<string> openLocalFile,
        Action<string, string>? showError)
        : base(fileService, localization, settings, sessionService)
    {
        Source = source;
        _renderService = renderService;
        _localization = localization;
        _settings = settings;
        _openLocalFile = openLocalFile;
        _showError = showError;
        UpdateTitle();
        Source.Saved += OnSourceSaved;
        Source.PropertyChanged += OnSourcePropertyChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    /// <summary>预览对应的源 Markdown 文档</summary>
    public DocumentViewModel Source { get; }

    /// <summary>预览 Tab 无可编辑内容，不参与热退出暂存与会话恢复</summary>
    protected override bool ParticipatesInHotExit => false;

    /// <summary>渲染结果控件树（MarkdownRenderService 产出，主题色走资源键自动跟随）</summary>
    [ObservableProperty]
    private FrameworkElement? _previewContent;

    private string? BaseDirectory =>
        Source.FilePath is null ? null : Path.GetDirectoryName(Source.FilePath);

    /// <summary>按源文档当前内容重建渲染结果；内容无变化时跳过（保存触发的刷新不闪烁）</summary>
    public void Refresh()
    {
        string text = Source.Editor.Text;
        if (text == _renderedText) return;
        _renderedText = text;
        PreviewContent = _renderService.Render(text, BaseDirectory, OnLinkClicked, _settings.FontFamily, _settings.FontSize);
    }

    /// <summary>编辑器字体/字号设置变更后强制重渲染（主 VM 原地更新同一 AppSettings 实例，此处直接取到新值）</summary>
    public void RefreshFonts()
    {
        _renderedText = null;
        Refresh();
    }

    /// <summary>源 Tab 关闭联动关闭本 Tab 前调用，解除事件订阅</summary>
    public void Detach()
    {
        Source.Saved -= OnSourceSaved;
        Source.PropertyChanged -= OnSourcePropertyChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnSourceSaved(object? sender, EventArgs e) => Refresh();

    // 源文件另存为后标题/目录变化：同步预览标题并按新目录重渲染（相对路径图片/链接基准变了）
    private void OnSourcePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Title) or nameof(FilePath))
        {
            UpdateTitle();
            _renderedText = null;
            Refresh();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateTitle();

    private void UpdateTitle()
    {
        Title = string.Format(_localization.GetString("Loc.Markdown.Preview.Title"), Source.Title);
    }

    private void OnLinkClicked(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ReportLinkError(url, ex.Message);
            }
            return;
        }

        string? path = null;
        if (uri is not null && uri.IsFile)
        {
            path = uri.LocalPath;
        }
        else if (uri is null && BaseDirectory is not null)
        {
            path = Path.GetFullPath(Path.Combine(BaseDirectory,
                Uri.UnescapeDataString(url.Replace('/', Path.DirectorySeparatorChar))));
        }

        if (path is null || !File.Exists(path))
        {
            ReportLinkError(url, _localization.GetString("Loc.Error.OpenLink.NotFound"));
            return;
        }
        _openLocalFile(path);
    }

    private void ReportLinkError(string url, string detail)
    {
        _showError?.Invoke(
            string.Format(_localization.GetString("Loc.Error.OpenLink.Message"), url, detail),
            _localization.GetString("Loc.Error.Title"));
    }
}
