using System.Drawing;
using System.Windows;
using System.Windows.Forms.Integration;
using ScintillaNET;
using Mnemosyne.Models;
using WinForms = System.Windows.Forms;
using SciStyle = ScintillaNET.Style;

namespace Mnemosyne.Controls;

/// <summary>
/// 对 ScintillaNET 的唯一封装点（architecture.md 4.2）。每个 Tab 一个实例。
/// 主题色全部取自当前主题资源字典（Color.Editor.* 键），主题切换时遍历实例重设。
/// </summary>
public class ScintillaHost : WindowsFormsHost
{
    private const int LineNumberMargin = 0;

    private static readonly List<WeakReference<ScintillaHost>> _instances = [];

    private readonly Scintilla _scintilla;
    private string _fontFamily = "Consolas";
    private double _fontSize = 13;
    private LanguageDefinition _language = LanguageRegistry.PlainText;

    /// <summary>内容或保存点变化（读取 IsDirty 获得最新状态）</summary>
    public event EventHandler? DirtyChanged;

    /// <summary>光标位置或选择变化（读取 CurrentLineNumber/CurrentColumn）</summary>
    public event EventHandler? CaretPositionChanged;

    /// <summary>编辑器内按键转发（WPF 命令在 WinForms 子控件聚焦时收不到快捷键，需要此桥接）</summary>
    public event EventHandler<WinForms.KeyEventArgs>? EditorKeyDown;

    public ScintillaHost()
    {
        _scintilla = new Scintilla
        {
            Dock = WinForms.DockStyle.Fill,
            BorderStyle = ScintillaNET.BorderStyle.None,
        };
        Child = _scintilla;

        _scintilla.WrapMode = WrapMode.None;
        _scintilla.IndentationGuides = IndentView.LookBoth;
        _scintilla.CaretLineLayer = Layer.UnderText;
        _scintilla.Margins[LineNumberMargin].Type = MarginType.Number;
        _scintilla.Margins[1].Width = 0;
        _scintilla.Margins[2].Width = 0;
        _scintilla.EndAtLastLine = true;
        _scintilla.ScrollWidthTracking = true;

        _scintilla.TextChanged += (_, _) =>
        {
            UpdateLineNumberMarginWidth();
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        };
        _scintilla.SavePointReached += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
        _scintilla.SavePointLeft += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
        _scintilla.UpdateUI += (_, e) =>
        {
            if (e.Change.HasFlag(UpdateChange.Selection) || e.Change.HasFlag(UpdateChange.Content))
            {
                CaretPositionChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        _scintilla.KeyDown += (_, e) =>
        {
            EditorKeyDown?.Invoke(this, e);
            if (e.Handled) e.SuppressKeyPress = true;
        };

        lock (_instances) _instances.Add(new WeakReference<ScintillaHost>(this));
    }

    public string Text
    {
        get => _scintilla.Text;
        set
        {
            _scintilla.Text = value;
            _scintilla.EmptyUndoBuffer();
            _scintilla.SetSavePoint();
        }
    }

    public bool IsDirty => _scintilla.Modified;

    public bool IsReadOnly
    {
        get => _scintilla.ReadOnly;
        set => _scintilla.ReadOnly = value;
    }

    public LanguageDefinition CurrentLanguage => _language;

    public int CurrentLineNumber => _scintilla.CurrentLine + 1;

    public int CurrentColumn => _scintilla.GetColumn(_scintilla.CurrentPosition) + 1;

    public LineEnding CurrentLineEnding => _scintilla.EolMode switch
    {
        Eol.Lf => LineEnding.Lf,
        Eol.Cr => LineEnding.Cr,
        _ => LineEnding.CrLf,
    };

    public void FocusEditor() => _scintilla.Focus();

    public void MarkSaved() => _scintilla.SetSavePoint();

    /// <summary>设置文档行尾模式并按需转换全文行尾符</summary>
    public void SetLineEnding(LineEnding ending, bool convert)
    {
        Eol mode = ending switch
        {
            LineEnding.Lf => Eol.Lf,
            LineEnding.Cr => Eol.Cr,
            _ => Eol.CrLf,
        };
        _scintilla.EolMode = mode;
        if (convert) _scintilla.ConvertEols(mode);
    }

    public void ApplyFont(string fontFamily, double fontSize)
    {
        _fontFamily = fontFamily;
        _fontSize = fontSize;
        ApplyTheme();
    }

    public void SetLanguage(LanguageDefinition language)
    {
        _language = language;
        // LexerName 仅接受 Lexilla 内部名称；空串回退到 null（纯文本）
        string lexerName = string.IsNullOrEmpty(language.LexerName) ? "null" : language.LexerName;
        try
        {
            _scintilla.LexerName = lexerName;
            if (_scintilla.LexerName != lexerName) _scintilla.LexerName = "null";
        }
        catch (Exception)
        {
            _scintilla.LexerName = "null";
        }
        if (!string.IsNullOrEmpty(language.Keywords)) _scintilla.SetKeywords(0, language.Keywords);
        if (!string.IsNullOrEmpty(language.SecondaryKeywords)) _scintilla.SetKeywords(1, language.SecondaryKeywords);
        ApplyTheme();
    }

    /// <summary>主题切换时由外部调用，遍历所有实例重设颜色</summary>
    public static void ApplyThemeToAll()
    {
        lock (_instances)
        {
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                if (_instances[i].TryGetTarget(out ScintillaHost? host))
                {
                    host.ApplyTheme();
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
        _scintilla.StyleResetDefault();
        _scintilla.Styles[SciStyle.Default].Font = _fontFamily;
        _scintilla.Styles[SciStyle.Default].SizeF = (float)_fontSize;
        _scintilla.Styles[SciStyle.Default].ForeColor = EditorColor("Foreground");
        _scintilla.Styles[SciStyle.Default].BackColor = EditorColor("Background");
        _scintilla.StyleClearAll();

        Color background = EditorColor("Background");
        _scintilla.CaretForeColor = EditorColor("Caret");
        // Scintilla v5：CaretLineVisible 已废弃，BackColor 带 alpha=255 即显示当前行高亮
        Color caretLine = EditorColor("CaretLine");
        _scintilla.CaretLineBackColor = Color.FromArgb(255, caretLine);
        _scintilla.SelectionBackColor = EditorColor("Selection");
        _scintilla.WhitespaceTextColor = EditorColor("Whitespace");
        _scintilla.WhitespaceBackColor = background;

        _scintilla.Styles[SciStyle.LineNumber].ForeColor = EditorColor("LineNumber");
        _scintilla.Styles[SciStyle.LineNumber].BackColor = background;
        _scintilla.Styles[SciStyle.IndentGuide].ForeColor = EditorColor("IndentGuide");
        _scintilla.Styles[SciStyle.IndentGuide].BackColor = background;
        _scintilla.Styles[SciStyle.BraceLight].ForeColor = EditorColor("Keyword");
        _scintilla.Styles[SciStyle.BraceLight].BackColor = background;
        _scintilla.Styles[SciStyle.BraceLight].Bold = true;
        _scintilla.Styles[SciStyle.BraceBad].ForeColor = EditorColor("Error");

        ApplyLexerStyles();
        UpdateLineNumberMarginWidth();
    }

    private void ApplyLexerStyles()
    {
        Color comment = EditorColor("Comment");
        Color keyword = EditorColor("Keyword");
        Color str = EditorColor("String");
        Color number = EditorColor("Number");
        Color preprocessor = EditorColor("Preprocessor");
        Color type = EditorColor("Type");
        Color function = EditorColor("Function");
        Color tag = EditorColor("Tag");
        Color attribute = EditorColor("Attribute");
        Color error = EditorColor("Error");
        Color fg = EditorColor("Foreground");

        void Set(int style, Color fore, bool bold = false)
        {
            _scintilla.Styles[style].ForeColor = fore;
            _scintilla.Styles[style].Bold = bold;
        }

        switch (_language.LexerName)
        {
            case "cpp":
                Set(SciStyle.Cpp.Comment, comment);
                Set(SciStyle.Cpp.CommentLine, comment);
                Set(SciStyle.Cpp.CommentDoc, comment);
                Set(SciStyle.Cpp.CommentLineDoc, comment);
                Set(SciStyle.Cpp.CommentDocKeyword, comment, bold: true);
                Set(SciStyle.Cpp.CommentDocKeywordError, comment);
                Set(SciStyle.Cpp.Word, keyword, bold: true);
                Set(SciStyle.Cpp.Word2, type);
                Set(SciStyle.Cpp.GlobalClass, type);
                Set(SciStyle.Cpp.Number, number);
                Set(SciStyle.Cpp.String, str);
                Set(SciStyle.Cpp.Character, str);
                Set(SciStyle.Cpp.Verbatim, str);
                Set(SciStyle.Cpp.StringRaw, str);
                Set(SciStyle.Cpp.TripleVerbatim, str);
                Set(SciStyle.Cpp.HashQuotedString, str);
                Set(SciStyle.Cpp.Preprocessor, preprocessor);
                Set(SciStyle.Cpp.PreprocessorComment, comment);
                Set(SciStyle.Cpp.PreprocessorCommentDoc, comment);
                Set(SciStyle.Cpp.Operator, fg);
                Set(SciStyle.Cpp.Regex, str);
                Set(SciStyle.Cpp.EscapeSequence, number);
                Set(SciStyle.Cpp.UserLiteral, str);
                Set(SciStyle.Cpp.TaskMarker, error, bold: true);
                break;

            case "python":
                Set(SciStyle.Python.CommentLine, comment);
                Set(SciStyle.Python.CommentBlock, comment);
                Set(SciStyle.Python.Number, number);
                Set(SciStyle.Python.String, str);
                Set(SciStyle.Python.Character, str);
                Set(SciStyle.Python.Triple, str);
                Set(SciStyle.Python.TripleDouble, str);
                Set(SciStyle.Python.Word, keyword, bold: true);
                Set(SciStyle.Python.Word2, type);
                Set(SciStyle.Python.ClassName, type, bold: true);
                Set(SciStyle.Python.DefName, function);
                Set(SciStyle.Python.Decorator, preprocessor);
                Set(SciStyle.Python.Operator, fg);
                break;

            case "xml":
            case "hypertext":
                Set(SciStyle.Xml.Tag, tag, bold: true);
                Set(SciStyle.Xml.TagEnd, tag, bold: true);
                Set(SciStyle.Xml.TagUnknown, tag);
                Set(SciStyle.Xml.Attribute, attribute);
                Set(SciStyle.Xml.AttributeUnknown, attribute);
                Set(SciStyle.Xml.Number, number);
                Set(SciStyle.Xml.DoubleString, str);
                Set(SciStyle.Xml.SingleString, str);
                Set(SciStyle.Xml.Comment, comment);
                Set(SciStyle.Xml.Entity, number);
                Set(SciStyle.Xml.Value, number);
                Set(SciStyle.Xml.XmlStart, preprocessor, bold: true);
                Set(SciStyle.Xml.XmlEnd, preprocessor, bold: true);
                Set(SciStyle.Xml.CData, preprocessor);
                Set(SciStyle.Xml.Question, preprocessor);
                Set(SciStyle.Xml.Script, fg);
                Set(SciStyle.Xml.XcComment, comment);
                if (_language.LexerName == "hypertext")
                {
                    Set(SciStyle.Html.Asp, preprocessor);
                    Set(SciStyle.Html.AspAt, preprocessor);
                }
                break;

            case "css":
                Set(SciStyle.Css.Comment, comment);
                Set(SciStyle.Css.Tag, tag, bold: true);
                Set(SciStyle.Css.Class, type);
                Set(SciStyle.Css.Id, type);
                Set(SciStyle.Css.PseudoClass, keyword);
                Set(SciStyle.Css.PseudoElement, keyword);
                Set(SciStyle.Css.Attribute, attribute);
                Set(SciStyle.Css.Identifier, attribute);
                Set(SciStyle.Css.Identifier2, keyword);
                Set(SciStyle.Css.Identifier3, number);
                Set(SciStyle.Css.Value, number);
                Set(SciStyle.Css.DoubleString, str);
                Set(SciStyle.Css.SingleString, str);
                Set(SciStyle.Css.Directive, preprocessor);
                Set(SciStyle.Css.Important, error, bold: true);
                Set(SciStyle.Css.Variable, type);
                Set(SciStyle.Css.Operator, fg);
                break;

            case "json":
                Set(SciStyle.Json.Number, number);
                Set(SciStyle.Json.String, str);
                Set(SciStyle.Json.PropertyName, attribute);
                Set(SciStyle.Json.EscapeSequence, number);
                Set(SciStyle.Json.LineComment, comment);
                Set(SciStyle.Json.BlockComment, comment);
                Set(SciStyle.Json.Keyword, keyword, bold: true);
                Set(SciStyle.Json.LdKeyword, keyword);
                Set(SciStyle.Json.Operator, fg);
                Set(SciStyle.Json.Uri, function);
                Set(SciStyle.Json.Error, error);
                break;

            case "markdown":
                Set(SciStyle.Markdown.Header1, keyword, bold: true);
                Set(SciStyle.Markdown.Header2, keyword, bold: true);
                Set(SciStyle.Markdown.Header3, keyword, bold: true);
                Set(SciStyle.Markdown.Header4, keyword, bold: true);
                Set(SciStyle.Markdown.Header5, keyword, bold: true);
                Set(SciStyle.Markdown.Header6, keyword, bold: true);
                Set(SciStyle.Markdown.Strong1, fg, bold: true);
                Set(SciStyle.Markdown.Strong2, fg, bold: true);
                Set(SciStyle.Markdown.Em1, type);
                Set(SciStyle.Markdown.Em2, type);
                _scintilla.Styles[SciStyle.Markdown.Em1].Italic = true;
                _scintilla.Styles[SciStyle.Markdown.Em2].Italic = true;
                Set(SciStyle.Markdown.UListItem, number);
                Set(SciStyle.Markdown.OListItem, number);
                Set(SciStyle.Markdown.BlockQuote, comment);
                Set(SciStyle.Markdown.Strikeout, comment);
                Set(SciStyle.Markdown.HRule, preprocessor);
                Set(SciStyle.Markdown.Link, function);
                _scintilla.Styles[SciStyle.Markdown.Link].Underline = true;
                Set(SciStyle.Markdown.Code, str);
                Set(SciStyle.Markdown.Code2, str);
                Set(SciStyle.Markdown.CodeBk, str);
                Set(SciStyle.Markdown.PreChar, preprocessor);
                break;

            case "sql":
            case "mssql":
            case "mysql":
                Set(SciStyle.Sql.Comment, comment);
                Set(SciStyle.Sql.CommentLine, comment);
                Set(SciStyle.Sql.CommentDoc, comment);
                Set(SciStyle.Sql.CommentLineDoc, comment);
                Set(SciStyle.Sql.Number, number);
                Set(SciStyle.Sql.Word, keyword, bold: true);
                Set(SciStyle.Sql.Word2, type);
                Set(SciStyle.Sql.String, str);
                Set(SciStyle.Sql.Character, str);
                Set(SciStyle.Sql.QuotedIdentifier, attribute);
                Set(SciStyle.Sql.Operator, fg);
                Set(SciStyle.Sql.User1, function);
                Set(SciStyle.Sql.Identifier, fg);
                break;

            case "powershell":
                Set(SciStyle.PowerShell.Comment, comment);
                Set(SciStyle.PowerShell.CommentStream, comment);
                Set(SciStyle.PowerShell.CommentDocKeyword, comment, bold: true);
                Set(SciStyle.PowerShell.String, str);
                Set(SciStyle.PowerShell.Character, str);
                Set(SciStyle.PowerShell.HereString, str);
                Set(SciStyle.PowerShell.HereCharacter, str);
                Set(SciStyle.PowerShell.Number, number);
                Set(SciStyle.PowerShell.Variable, type);
                Set(SciStyle.PowerShell.Keyword, keyword, bold: true);
                Set(SciStyle.PowerShell.Cmdlet, function);
                Set(SciStyle.PowerShell.Alias, function);
                Set(SciStyle.PowerShell.Function, function);
                Set(SciStyle.PowerShell.Operator, fg);
                break;

            case "batch":
                Set(SciStyle.Batch.Comment, comment);
                Set(SciStyle.Batch.Word, keyword, bold: true);
                Set(SciStyle.Batch.Label, type);
                Set(SciStyle.Batch.Hide, comment);
                Set(SciStyle.Batch.Command, function);
                Set(SciStyle.Batch.Identifier, type);
                Set(SciStyle.Batch.Operator, fg);
                break;

            case "props":
                Set(SciStyle.Properties.Comment, comment);
                Set(SciStyle.Properties.Section, type, bold: true);
                Set(SciStyle.Properties.Assignment, fg);
                Set(SciStyle.Properties.Key, attribute);
                Set(SciStyle.Properties.DefVal, str);
                break;

            case "vb":
            case "vbscript":
                Set(SciStyle.Vb.Comment, comment);
                Set(SciStyle.Vb.CommentBlock, comment);
                Set(SciStyle.Vb.DocLine, comment);
                Set(SciStyle.Vb.DocBlock, comment);
                Set(SciStyle.Vb.Number, number);
                Set(SciStyle.Vb.HexNumber, number);
                Set(SciStyle.Vb.BinNumber, number);
                Set(SciStyle.Vb.Keyword, keyword, bold: true);
                Set(SciStyle.Vb.Keyword2, type);
                Set(SciStyle.Vb.Keyword3, function);
                Set(SciStyle.Vb.Keyword4, preprocessor);
                Set(SciStyle.Vb.String, str);
                Set(SciStyle.Vb.Preprocessor, preprocessor);
                Set(SciStyle.Vb.Constant, number);
                Set(SciStyle.Vb.Date, number);
                Set(SciStyle.Vb.Operator, fg);
                break;

            case "ruby":
                Set(SciStyle.Ruby.CommentLine, comment);
                Set(SciStyle.Ruby.Pod, comment);
                Set(SciStyle.Ruby.Number, number);
                Set(SciStyle.Ruby.Word, keyword, bold: true);
                Set(SciStyle.Ruby.WordDemoted, keyword);
                Set(SciStyle.Ruby.String, str);
                Set(SciStyle.Ruby.Character, str);
                Set(SciStyle.Ruby.HereDelim, str);
                Set(SciStyle.Ruby.HereQ, str);
                Set(SciStyle.Ruby.HereQq, str);
                Set(SciStyle.Ruby.HereQx, str);
                Set(SciStyle.Ruby.StringQ, str);
                Set(SciStyle.Ruby.StringQq, str);
                Set(SciStyle.Ruby.StringQx, str);
                Set(SciStyle.Ruby.StringQr, str);
                Set(SciStyle.Ruby.StringQw, str);
                Set(SciStyle.Ruby.Regex, str);
                Set(SciStyle.Ruby.ClassName, type, bold: true);
                Set(SciStyle.Ruby.DefName, function);
                Set(SciStyle.Ruby.ModuleName, type);
                Set(SciStyle.Ruby.Symbol, number);
                Set(SciStyle.Ruby.Global, type);
                Set(SciStyle.Ruby.InstanceVar, type);
                Set(SciStyle.Ruby.ClassVar, type);
                Set(SciStyle.Ruby.BackTicks, preprocessor);
                Set(SciStyle.Ruby.Operator, fg);
                Set(SciStyle.Ruby.Error, error);
                break;

            case "bash":
                Set(2, comment);            // SCE_SH_COMMENTLINE
                Set(3, number);             // SCE_SH_NUMBER
                Set(4, keyword, bold: true);// SCE_SH_WORD
                Set(5, str);                // SCE_SH_STRING
                Set(6, str);                // SCE_SH_CHARACTER
                Set(7, fg);                 // SCE_SH_OPERATOR
                Set(9, type);               // SCE_SH_SCALAR
                Set(10, type);              // SCE_SH_PARAM
                Set(11, str);               // SCE_SH_BACKTICKS
                Set(12, str);               // SCE_SH_HERE_DELIM
                Set(13, str);               // SCE_SH_HERE_Q
                Set(1, error);              // SCE_SH_ERROR
                break;

            case "yaml":
                Set(1, comment);            // SCE_YAML_COMMENT
                Set(3, keyword, bold: true);// SCE_YAML_KEYWORD
                Set(4, number);             // SCE_YAML_NUMBER
                Set(5, type);               // SCE_YAML_REFERENCE
                Set(6, preprocessor);       // SCE_YAML_DOCUMENT
                Set(7, str);                // SCE_YAML_TEXT
                Set(8, error);              // SCE_YAML_ERROR
                Set(9, fg);                 // SCE_YAML_OPERATOR
                break;

            case "toml":
                Set(1, comment);            // SCE_TOML_COMMENT
                Set(3, keyword, bold: true);// SCE_TOML_KEYWORD
                Set(4, number);             // SCE_TOML_NUMBER
                Set(5, attribute);          // SCE_TOML_KEY
                Set(6, type, bold: true);   // SCE_TOML_TABLE
                Set(7, str);                // SCE_TOML_STRING
                Set(8, str);                // SCE_TOML_CHARACTER
                Set(10, number);            // SCE_TOML_DATETIME
                Set(11, fg);                // SCE_TOML_OPERATOR
                break;

            case "lua":
                Set(1, comment);            // SCE_LUA_COMMENT
                Set(2, comment);            // SCE_LUA_COMMENTLINE
                Set(3, comment);            // SCE_LUA_COMMENTDOC
                Set(4, number);             // SCE_LUA_NUMBER
                Set(5, keyword, bold: true);// SCE_LUA_WORD
                Set(6, str);                // SCE_LUA_STRING
                Set(7, str);                // SCE_LUA_CHARACTER
                Set(8, str);                // SCE_LUA_LITERALSTRING
                Set(9, preprocessor);       // SCE_LUA_PREPROCESSOR
                Set(10, fg);                // SCE_LUA_OPERATOR
                Set(13, type);              // SCE_LUA_WORD2
                Set(19, function);          // SCE_LUA_WORD5
                break;

            case "rust":
                Set(1, comment);            // SCE_RUST_COMMENTBLOCK
                Set(2, comment);            // SCE_RUST_COMMENTLINE
                Set(3, comment);            // SCE_RUST_COMMENTBLOCKDOC
                Set(4, comment);            // SCE_RUST_COMMENTLINEDOC
                Set(5, number);             // SCE_RUST_NUMBER
                Set(6, keyword, bold: true);// SCE_RUST_WORD
                Set(7, type);               // SCE_RUST_WORD2
                Set(13, str);               // SCE_RUST_STRING
                Set(14, str);               // SCE_RUST_STRINGR
                Set(15, str);               // SCE_RUST_CHARACTER
                Set(16, fg);                // SCE_RUST_OPERATOR
                Set(18, type);              // SCE_RUST_LIFETIME
                Set(19, preprocessor);      // SCE_RUST_MACRO
                Set(20, error);             // SCE_RUST_LEXERROR
                break;

            case "diff":
                Set(1, comment);            // SCE_DIFF_COMMENT
                Set(2, keyword, bold: true);// SCE_DIFF_COMMAND
                Set(3, preprocessor, bold: true); // SCE_DIFF_HEADER
                Set(4, number);             // SCE_DIFF_POSITION
                Set(5, error);              // SCE_DIFF_DELETED
                Set(6, type);               // SCE_DIFF_ADDED
                Set(7, function);           // SCE_DIFF_CHANGED
                break;

            case "registry":
                Set(1, comment);            // SCE_REG_COMMENT
                Set(2, attribute);          // SCE_REG_VALUENAME
                Set(3, str);                // SCE_REG_STRING
                Set(4, number);             // SCE_REG_HEXDIGIT
                Set(5, type);               // SCE_REG_VALUETYPE
                Set(6, fg);                 // SCE_REG_OPERATOR
                Set(7, number);             // SCE_REG_GUID
                Set(10, number);            // SCE_REG_ESCAPED
                break;

            case "cmake":
                Set(1, comment);            // SCE_CMAKE_COMMENT
                Set(2, str);                // SCE_CMAKE_STRINGDQ
                Set(3, str);                // SCE_CMAKE_STRINGLQ
                Set(4, str);                // SCE_CMAKE_STRINGRQ
                Set(5, function);           // SCE_CMAKE_COMMANDS
                Set(6, fg);                 // SCE_CMAKE_PARAMETERS
                Set(7, type);               // SCE_CMAKE_VARIABLE
                Set(13, type);              // SCE_CMAKE_STRINGVAR
                Set(14, number);            // SCE_CMAKE_NUMBER
                break;

            case "perl":
                Set(SciStyle.Perl.CommentLine, comment);
                Set(SciStyle.Perl.Pod, comment);
                Set(SciStyle.Perl.Number, number);
                Set(SciStyle.Perl.Word, keyword, bold: true);
                Set(SciStyle.Perl.String, str);
                Set(SciStyle.Perl.Character, str);
                Set(SciStyle.Perl.Scalar, type);
                Set(SciStyle.Perl.Array, type);
                Set(SciStyle.Perl.Hash, type);
                Set(SciStyle.Perl.Regex, str);
                Set(SciStyle.Perl.Preprocessor, preprocessor);
                Set(SciStyle.Perl.Operator, fg);
                Set(SciStyle.Perl.Error, error);
                break;

            case "nsis":
                Set(1, comment);            // SCE_NSIS_COMMENT
                Set(2, str);                // SCE_NSIS_STRINGDQ
                Set(3, str);                // SCE_NSIS_STRINGLQ
                Set(4, str);                // SCE_NSIS_STRINGRQ
                Set(5, keyword, bold: true);// SCE_NSIS_FUNCTION
                Set(6, type);               // SCE_NSIS_VARIABLE
                Set(13, type);              // SCE_NSIS_STRINGVAR
                Set(14, number);            // SCE_NSIS_NUMBER
                Set(9, type, bold: true);   // SCE_NSIS_SECTIONDEF
                break;
        }
    }

    private void UpdateLineNumberMarginWidth()
    {
        int digits = Math.Max(2, _scintilla.Lines.Count.ToString().Length);
        int width = _scintilla.TextWidth(SciStyle.LineNumber, new string('9', digits)) + 12;
        if (_scintilla.Margins[LineNumberMargin].Width != width)
        {
            _scintilla.Margins[LineNumberMargin].Width = width;
        }
    }

    private static Color EditorColor(string name)
    {
        object? resource = Application.Current?.TryFindResource("Color.Editor." + name);
        if (resource is System.Windows.Media.Color mediaColor)
        {
            return Color.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        }
        return Color.Magenta;
    }

    /// <summary>
    /// Lexilla 的静态方法在首个 Scintilla 控件创建原生句柄前不可用（内部委托尚未初始化），
    /// 语言选择弹窗可能在没有任何文档时打开，因此先做一次预热。
    /// </summary>
    public static IReadOnlyList<string> GetAvailableLexerNames()
    {
        try
        {
            return Lexilla.GetLexerNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception)
        {
            try
            {
                using var warmup = new Scintilla();
                _ = warmup.Handle;
                return Lexilla.GetLexerNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception)
            {
                return [];
            }
        }
    }
}
