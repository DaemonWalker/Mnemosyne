using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Languages.Core;

/// <summary>
/// 内置语言包：全部 Lexilla 系语言定义（扩展名 ↔ Lexer 映射 + 关键字表）。
/// LexerName 为 Lexilla 的内部名称；关键字表在设置 Lexer 时通过 SCI_SETKEYWORDS 提供。
/// Lexilla（5.5.0）无 go/kotlin/swift/objectivec/groovy/scala 独立 Lexer，复用 cpp + 各自关键字表。
/// </summary>
public sealed class CoreLanguages : IMnemosynePlugin, ILanguageContribution
{
    public string Id => "mnemosyne.languages.core";

    public string DisplayName => "Core Languages";

    public string Version => "1.0.0";

    public string Description => "内置语言高亮包：37 种 Lexilla 系语言的扩展名映射与关键字表。";

    public IReadOnlyList<PluginSettingDescriptor> Settings => [];

    public void Initialize(IPluginContext context)
    {
    }

    public IReadOnlyList<PluginLanguageDefinition> Languages { get; } =
    [
        new("C#", "cpp", ["cs", "csx"], KeywordsCSharp, supportsFolding: true),
        new("C++", "cpp", ["cpp", "cxx", "cc", "hpp", "hxx", "h", "inl"], KeywordsCpp, supportsFolding: true),
        new("C", "cpp", ["c"], KeywordsC, supportsFolding: true),
        new("Java", "cpp", ["java"], KeywordsJava, supportsFolding: true),
        new("JavaScript", "cpp", ["js", "mjs", "cjs", "jsx"], KeywordsJavaScript, supportsFolding: true),
        new("TypeScript", "cpp", ["ts", "tsx", "mts", "cts"], KeywordsTypeScript, supportsFolding: true),
        new("Python", "python", ["py", "pyw", "pyi"], KeywordsPython, supportsFolding: true),
        new("XML", "xml", ["xml", "xaml", "xsl", "xslt", "svg", "config", "csproj", "props", "targets", "resx", "settings", "nuspec", "slnx"], formatterId: "xml", supportsFolding: true),
        new("HTML", "hypertext", ["html", "htm", "shtml", "xhtml", "vue", "cshtml", "razor"], formatterId: "html", supportsFolding: true),
        new("CSS", "css", ["css", "scss", "less"], supportsFolding: true),
        new("JSON", "json", ["json", "jsonc", "json5", "ipynb"], formatterId: "json", supportsFolding: true),
        new("Markdown", "markdown", ["md", "markdown", "mdown"]),
        new("SQL", "sql", ["sql"], KeywordsSql, supportsFolding: true),
        new("PowerShell", "powershell", ["ps1", "psm1", "psd1"], KeywordsPowerShell, supportsFolding: true),
        new("Batch", "batch", ["bat", "cmd"], KeywordsBatch),
        new("INI", "props", ["ini", "inf", "cfg", "properties", "editorconfig", "gitignore", "gitattributes", "env"], supportsFolding: true),
        new("YAML", "yaml", ["yml", "yaml"], supportsFolding: true),
        new("TOML", "toml", ["toml"], supportsFolding: true),
        new("Visual Basic", "vb", ["vb", "vbs", "bas", "frm", "cls"], KeywordsVb, supportsFolding: true),
        new("PHP", "phpscript", ["php", "php3", "php4", "php5", "phtml"], KeywordsPhp, supportsFolding: true),
        new("Ruby", "ruby", ["rb", "rbw", "rake", "gemspec"], KeywordsRuby, supportsFolding: true),
        new("Bash", "bash", ["sh", "bash", "zsh", "ksh"], KeywordsBash, supportsFolding: true),
        new("Lua", "lua", ["lua"], KeywordsLua, supportsFolding: true),
        new("Rust", "rust", ["rs"], KeywordsRust, supportsFolding: true),
        new("Go", "cpp", ["go"], KeywordsGo, KeywordsGoBuiltin, supportsFolding: true),
        new("Kotlin", "cpp", ["kt", "kts"], KeywordsKotlin, KeywordsKotlinType, supportsFolding: true),
        new("Swift", "cpp", ["swift"], KeywordsSwift, supportsFolding: true),
        new("Objective-C", "cpp", ["m", "mm"], KeywordsObjectiveC, supportsFolding: true),
        new("Groovy", "cpp", ["groovy", "gvy", "gradle"], KeywordsGroovy, supportsFolding: true),
        new("Scala", "cpp", ["scala", "sc"], KeywordsScala, supportsFolding: true),
        new("Dart", "dart", ["dart"], KeywordsDart, KeywordsDartType, supportsFolding: true),
        new("Haskell", "haskell", ["hs", "lhs"], KeywordsHaskell, supportsFolding: true),
        new("R", "r", ["r"], KeywordsR, supportsFolding: true),
        new("CoffeeScript", "coffeescript", ["coffee"], KeywordsCoffeeScript, KeywordsCoffeeScriptGlobal, supportsFolding: true),
        new("Diff", "diff", ["diff", "patch"], supportsFolding: true),
        new("Registry", "registry", ["reg"], supportsFolding: true),
        new("CMake", "cmake", ["cmake"], KeywordsCmake, supportsFolding: true, fileNames: ["CMakeLists.txt"]),
        new("Perl", "perl", ["pl", "pm", "pod"], KeywordsPerl, supportsFolding: true),
        new("F#", "fsharp", ["fs", "fsx", "fsi"], KeywordsFSharp),
        new("Nsis", "nsis", ["nsi", "nsh"], supportsFolding: true),
        new("Makefile", "makefile", [], fileNames: ["Makefile"]),
        new("Dockerfile", "conf", [], fileNames: ["Dockerfile"]),
    ];

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

    private const string KeywordsGo =
        "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var";

    private const string KeywordsGoBuiltin =
        "any append bool byte cap close comparable complex complex64 complex128 copy delete error false float32 float64 imag int int8 int16 int32 int64 iota len make new nil panic print println real recover rune string true uint uint8 uint16 uint32 uint64 uintptr";

    private const string KeywordsKotlin =
        "abstract actual annotation as break by catch class companion const constructor continue crossinline data delegate do dynamic else enum expect external false final finally for fun get if import in infix init inline inner interface internal is lateinit noinline null object open operator out override package private protected public reified return sealed set super suspend tailrec this throw true try typealias typeof val var vararg when where while";

    private const string KeywordsKotlinType =
        "Any Boolean Byte Char CharSequence Double Float Int Long Nothing Short String Unit Array BooleanArray ByteArray CharArray DoubleArray FloatArray IntArray LongArray ShortArray List MutableList Map MutableMap Set MutableSet";

    private const string KeywordsSwift =
        "Any as associatedtype associativity async await break case catch class continue convenience default defer deinit didSet do dynamic else enum extension fallthrough false fileprivate final for func get guard if import in indirect infix init inout internal is lazy left let mutating nil none open operator optional override postfix precedence prefix private protocol public repeat required rethrows return right self Self set some static struct subscript super switch throw throws true try typealias unowned var weak where while willSet";

    private const string KeywordsObjectiveC =
        "auto break case catch char class const const_cast continue default do double dynamic_cast else enum explicit extern false float for goto if inline int long mutable namespace new nil Nil NO nullptr operator private protected public register reinterpret_cast restrict return self short signed sizeof static static_assert struct super switch template this throw true try typedef typeid typename union unsigned using virtual void volatile wchar_t while YES id SEL IMP BOOL in out inout bycopy byref oneway";

    private const string KeywordsGroovy =
        "abstract as assert boolean break byte case catch char class const continue def default do double else enum extends false final finally float for goto if implements import in instanceof int interface long native new null package private protected public record return sealed short static strictfp super switch synchronized this threadsafe throw throws trait transient true try var void volatile while yield";

    private const string KeywordsScala =
        "abstract case catch class def do else extends false final finally for forSome if implicit import lazy match new null object override package private protected return sealed super this throw trait true try type val var while with yield";

    // dart lexer 有四个关键字表（primary/secondary/tertiary/types），PluginLanguageDefinition 只有两个槽位：
    // 语言关键字进表 0，内置类型进表 1（SCE_DART_KW_SECONDARY），配色按类型色处理
    private const string KeywordsDart =
        "abstract as assert async await break case catch class const continue covariant default deferred do dynamic else enum export extends extension external factory false final finally for get hide if implements import in interface is late library mixin new null on operator part required rethrow return set show static super switch sync this throw true try typedef var void while with yield";

    private const string KeywordsDartType =
        "bool double int num Object String List Map Set Iterable Iterator Future Stream Duration DateTime Function Never Null Record Symbol Type Comparable Pattern RegExp Error Exception StackTrace";

    private const string KeywordsHaskell =
        "as case class data default deriving do else family foreign hiding if import in infix infixl infixr instance let mdo module newtype of proc qualified rec then type where";

    private const string KeywordsR =
        "if else repeat while function for in next break TRUE FALSE NULL NA Inf NaN NA_integer_ NA_real_ NA_complex_ NA_character_ T F";

    private const string KeywordsCoffeeScript =
        "and break by catch class continue debugger delete do else extends false finally for if in instanceof is isnt loop new no not of off on or return super switch then this throw true try typeof undefined unless until when while yes";

    private const string KeywordsCoffeeScriptGlobal =
        "exports module require global process window document console";
}
