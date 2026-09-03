using System.IO;

namespace Mnemosyne.Models;

/// <summary>
/// 扩展名 ↔ Lexer 映射表（architecture.md 约定集中放 Models）。
/// LexerName 为 Lexilla 的内部名称；关键字表在设置 Lexer 时通过 SCI_SETKEYWORDS 提供。
/// </summary>
public static class LanguageRegistry
{
    public static LanguageDefinition PlainText { get; } = new("Plain Text", "", ["txt", "log", "text"]);

    public static IReadOnlyList<LanguageDefinition> All { get; } =
    [
        new("C#", "cpp", ["cs", "csx"], KeywordsCSharp),
        new("C++", "cpp", ["cpp", "cxx", "cc", "hpp", "hxx", "h", "inl"], KeywordsCpp),
        new("C", "cpp", ["c"], KeywordsC),
        new("Java", "cpp", ["java"], KeywordsJava),
        new("JavaScript", "cpp", ["js", "mjs", "cjs", "jsx"], KeywordsJavaScript),
        new("TypeScript", "cpp", ["ts", "tsx", "mts", "cts"], KeywordsTypeScript),
        new("Python", "python", ["py", "pyw", "pyi"], KeywordsPython),
        new("XML", "xml", ["xml", "xaml", "xsl", "xslt", "svg", "config", "csproj", "props", "targets", "resx", "settings", "nuspec", "slnx"]),
        new("HTML", "hypertext", ["html", "htm", "shtml", "xhtml", "vue", "cshtml", "razor"]),
        new("CSS", "css", ["css", "scss", "less"]),
        new("JSON", "json", ["json", "jsonc", "json5", "ipynb"]),
        new("Markdown", "markdown", ["md", "markdown", "mdown"]),
        new("SQL", "sql", ["sql"], KeywordsSql),
        new("PowerShell", "powershell", ["ps1", "psm1", "psd1"], KeywordsPowerShell),
        new("Batch", "batch", ["bat", "cmd"], KeywordsBatch),
        new("INI", "props", ["ini", "inf", "cfg", "properties", "editorconfig", "gitignore", "gitattributes", "env"]),
        new("YAML", "yaml", ["yml", "yaml"]),
        new("TOML", "toml", ["toml"]),
        new("Visual Basic", "vb", ["vb", "vbs", "bas", "frm", "cls"], KeywordsVb),
        new("PHP", "phpscript", ["php", "php3", "php4", "php5", "phtml"], KeywordsPhp),
        new("Ruby", "ruby", ["rb", "rbw", "rake", "gemspec"], KeywordsRuby),
        new("Bash", "bash", ["sh", "bash", "zsh", "ksh"], KeywordsBash),
        new("Lua", "lua", ["lua"], KeywordsLua),
        new("Rust", "rust", ["rs"], KeywordsRust),
        new("Diff", "diff", ["diff", "patch"]),
        new("Registry", "registry", ["reg"]),
        new("CMake", "cmake", ["cmake"], KeywordsCmake),
        new("Perl", "perl", ["pl", "pm", "pod"], KeywordsPerl),
        new("F#", "fsharp", ["fs", "fsx", "fsi"], KeywordsFSharp),
        new("Nsis", "nsis", ["nsi", "nsh"]),
    ];

    private static readonly Dictionary<string, LanguageDefinition> _byExtension = BuildExtensionIndex();

    private static readonly Dictionary<string, LanguageDefinition> _byFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CMakeLists.txt"] = All.First(l => l.LexerName == "cmake"),
        ["Makefile"] = new("Makefile", "makefile", []),
        ["Dockerfile"] = new("Dockerfile", "conf", []),
    };

    /// <summary>按文件路径（文件名优先、其次扩展名）匹配语言；无匹配返回纯文本。</summary>
    public static LanguageDefinition GetForFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return PlainText;
        string fileName = Path.GetFileName(filePath);
        if (_byFileName.TryGetValue(fileName, out LanguageDefinition? byName)) return byName;
        string ext = Path.GetExtension(fileName);
        if (ext.Length > 1 && _byExtension.TryGetValue(ext[1..], out LanguageDefinition? byExt)) return byExt;
        return PlainText;
    }

    /// <summary>按 Lexilla 内部名称查找已注册语言；未注册返回 null（由调用方按原始名称建立条目）。</summary>
    public static LanguageDefinition? GetByLexerName(string lexerName)
    {
        if (string.IsNullOrEmpty(lexerName)) return PlainText;
        return All.FirstOrDefault(l => string.Equals(l.LexerName, lexerName, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, LanguageDefinition> BuildExtensionIndex()
    {
        var map = new Dictionary<string, LanguageDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (LanguageDefinition lang in All)
        {
            foreach (string ext in lang.Extensions)
            {
                map[ext] = lang;
            }
        }
        return map;
    }

    private const string KeywordsCSharp =
        "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while async await var when nameof record init partial get set add remove yield dynamic required file scoped unmanaged nint nuint notnull and or not with";

    private const string KeywordsCpp =
        "auto bool break case catch char char8_t char16_t char32_t class const constexpr consteval constinit const_cast continue co_await co_return co_yield decltype default delete do double dynamic_cast else enum explicit export extern false final float for friend goto if inline int long mutable namespace new noexcept nullptr operator override private protected public register reinterpret_cast requires return short signed sizeof static static_assert static_cast struct switch template this thread_local throw true try typedef typeid typename union unsigned using virtual void volatile wchar_t while and and_eq bitand bitor compl not not_eq or or_eq xor xor_eq concept import module alignas alignof";

    private const string KeywordsC =
        "auto break case char const continue default do double else enum extern float for goto if inline int long register restrict return short signed sizeof static struct switch typedef union unsigned void volatile while _Alignas _Alignof _Atomic _Bool _Complex _Generic _Imaginary _Noreturn _Static_assert _Thread_local";

    private const string KeywordsJava =
        "abstract assert boolean break byte case catch char class const continue default do double else enum extends final finally float for goto if implements import instanceof int interface long native new package private protected public record return sealed short static strictfp super switch synchronized this throw throws transient try var void volatile while true false null yield permits";

    private const string KeywordsJavaScript =
        "async await break case catch class const continue debugger default delete do else export extends finally for function if import in instanceof let new of return static super switch this throw try typeof var void while with yield true false null undefined get set";

    private const string KeywordsTypeScript =
        "abstract any as asserts async await bigint boolean break case catch class const constructor continue debugger declare default delete do else enum export extends false finally for from function get if implements import in infer instanceof interface is keyof let module namespace never new null number object of out override package private protected public readonly require return set static string super switch symbol this throw true try type typeof undefined unique unknown var void while with yield";

    private const string KeywordsPython =
        "and as assert async await break class continue def del elif else except finally for from global if import in is lambda match nonlocal not or pass raise return try while with yield False None True";

    private const string KeywordsSql =
        "add alter and as asc begin between by case check column commit constraint create cross cursor database declare default delete desc distinct drop else end escape except exists fetch foreign from full group having if in index inner insert intersect into is join key left like limit not null on or order outer primary procedure references right rollback select set table then top union unique update values view when where with";

    private const string KeywordsPowerShell =
        "begin break catch class continue data define do dynamicparam else elseif end enum exit filter finally for foreach from function hidden if in param process return static switch throw trap try until using var while workflow";

    private const string KeywordsBatch =
        "call cd chdir choice cls copy defined del dir do echo else endlocal erase exist exit for goto if in md mkdir move not pause popd pushd rd rem ren rename rmdir set setlocal shift start title type ver verify vol";

    private const string KeywordsVb =
        "addhandler addressof alias and andalso as boolean byref byval call case catch cbool cbyte cchar cdate cdbl cdec char cint class clng cobj const continue csbyte cshort csng cstr ctype cuint culng cushort date decimal declare default delegate dim directcast do double each else elseif end endif enum erase error event exit finally for friend function get gettype goto handles if implements imports in inherits integer interface is isnot let lib like long loop me mod module mustinherit mustoverride mybase myclass namespace new next not nothing notinheritable notoverridable object of on operator option optional or orelse overrides paramarray partial private property protected public raiseevent readonly redim rem removehandler resume return sbyte select set shadows shared short single static step stop string structure sub synclock then throw to true try trycast typeof uinteger ulong ushort using variant wend when while with withevents writeonly xor";

    private const string KeywordsPhp =
        "abstract and array as break callable case catch class clone const continue declare default do echo else elseif empty enddeclare endfor endforeach endif endswitch endwhile extends final finally fn for foreach function global goto if implements include include_once instanceof insteadof interface isset list match namespace new or print private protected public require require_once return static switch throw trait try unset use var while xor yield yieldfrom true false null";

    private const string KeywordsRuby =
        "alias and begin break case class def defined do else elsif end ensure false for if in module next nil not or redo rescue retry return self super then true undef unless until when while yield";

    private const string KeywordsBash =
        "alias bg bind break builtin caller case cd command compgen complete compopt continue coproc declare dirs disown do done echo elif else enable esac eval exec exit export false fc fg fi for function getopts hash help history if in jobs kill let local logout mapfile popd printf pushd pwd read readonly return select set shift shopt source suspend test then time times trap true type typeset ulimit umask unalias unset until wait while";

    private const string KeywordsLua =
        "and break do else elseif end false for function goto if in local nil not or repeat return then true until while";

    private const string KeywordsRust =
        "as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait true type unsafe use where while";

    private const string KeywordsCmake =
        "add_compile_options add_custom_command add_custom_target add_definitions add_dependencies add_executable add_library add_subdirectory add_test aux_source_directory break build_command cmake_minimum_required cmake_policy configure_file continue create_test_sourcelist define_property elseif else enable_language enable_testing endforeach endfunction endif endmacro endwhile execute_process export file find_file find_library find_package find_path find_program fltk_wrap_ui foreach function get_cmake_property get_directory_property get_filename_component get_property get_source_file_property get_target_property get_test_property if include include_directories include_external_msproject include_regular_expression install link_directories link_libraries list load_cache load_command macro mark_as_advanced math message option project qt_wrap_cpp qt_wrap_ui remove_definitions return separate_arguments set set_directory_properties set_property set_source_files_properties set_target_properties set_tests_properties site_name source_group string target_compile_definitions target_compile_options target_include_directories target_link_libraries try_compile try_run unset variable_watch while";

    private const string KeywordsPerl =
        "abs accept alarm and atan2 bind binmode bless caller chdir chmod chomp chop chown chr chroot close closedir cmp connect continue cos crypt dbmclose dbmopen defined delete die do dump each else elsif endgrent endhostent endnetent endprotoent endpwent endservent eof eq eval exec exists exit exp fcntl fileno flock for foreach fork format formline ge getc getgrent getgrgid getgrnam gethostbyaddr gethostbyname gethostent getlogin getnetbyaddr getnetbyname getnetent getpeername getpgrp getppid getpriority getprotobyname getprotobynumber getprotoent getpwent getpwnam getpwuid getservbyname getservbyport getservent getsockname getsockopt given glob gmtime goto grep gt hex if index int ioctl join keys kill last lc lcfirst le length link listen local localtime log lstat lt map mkdir msgctl msgget msgrcv msgsnd my ne next no not oct open opendir or ord our pack package pipe pop pos print printf prototype push quotemeta rand read readdir readline readlink readpipe recv redo ref rename require reset return reverse rewinddir rindex rmdir say scalar seek seekdir select semctl semget semop send setgrent sethostent setnetent setpgrp setpriority setprotoent setpwent setservent setsockopt shift shmctl shmget shmread shmwrite shutdown sin sleep socket socketpair sort splice split sprintf sqrt srand stat state study sub substr symlink syscall sysopen sysread sysseek system syswrite tell telldir tie tied time times tr truncate uc ucfirst umask undef unless unlink unpack unshift untie until use utime values vec wait waitpid wantarray warn when while write xor";

    private const string KeywordsFSharp =
        "abstract and as assert base begin class default delegate do done downcast downto elif else end exception extern false finally fixed for fun function global if in inherit inline interface internal lazy let match member module mutable namespace new null of open or override private public rec return sig static struct then to true try type upcast use val void when while with yield";
}
