# Mnemosyne 技术架构

> 实现时遵循本文档的目录与分层约定。细节（类名、方法签名）以实现时为准，但分层和依赖方向不得违反。

## 1. 解决方案结构

```
Mnemosyne.sln
src/
  Mnemosyne/                        # WPF 主程序（net10.0-windows）
  Mnemosyne.Plugin.Abstractions/    # 插件接口（netstandard2.0，插件与主程序共同引用）
plugins/                            # 内置格式化插件，构建输出到主程序 plugins/ 目录
  Mnemosyne.Formatters.Json/
  Mnemosyne.Formatters.Xml/
  Mnemosyne.Formatters.Html/
docs/                               # 本文档目录
```

- `Mnemosyne.Plugin.Abstractions` 用 netstandard2.0，保证第三方插件可用任意 .NET 版本编写
- 三个内置格式化器引用 Abstractions，**不引用主程序**，模拟真实插件
- 主程序通过反射加载插件，**不直接引用**插件项目（构建事件拷贝 dll 到输出目录 `plugins/` 即可）

## 2. NuGet 依赖

| 包 | 用途 |
|---|---|
| Scintilla5.NET | 编辑器内核（Scintilla v5 封装） |
| Markdig | Markdown 解析 |
| UTF.Unknown | 编码自动探测（UDE） |
| CommunityToolkit.Mvvm | MVVM（源生成器，无反射开销） |
| XtermSharp | VT 解析与终端屏幕缓冲（集成终端，唯一新增依赖） |

配置序列化用内置 `System.Text.Json`，不引第三方。

## 3. 主程序内部分层

```
src/Mnemosyne/
  App.xaml(.cs)              # 启动、单实例、命令行解析、主题/语言初始化
  Views/                     # 窗口与用户控件（XAML + 少量 code-behind）
  ViewModels/                # CommunityToolkit.Mvvm，[ObservableProperty]/[RelayCommand]
                               终端为容器 + tab 两层：TerminalViewModel 管 tab 集合，TerminalTabViewModel 管单会话
  Controls/
    ScintillaHost.cs         # 对 ScintillaNET 的唯一封装点，Views 不直接接触 ScintillaNET 类型
    TerminalControl.cs       # 终端自绘渲染控件（纯 WPF，无 HWND 空域问题）
  Services/
    Terminal/                # ConPTY 后端（ConPtyNative P/Invoke、ConPtySession）与终端会话（TerminalSession 接 XtermSharp）
    ConfigService.cs         # settings.json 读写（便携模式：exe 同目录）
    FileService.cs           # 打开/保存/编码检测/分块读取
    SearchService.cs         # 页内搜索 + 文件夹扫描（后台 Task）
    PluginService.cs         # 插件发现与加载，单个插件异常不影响主程序
    SessionService.cs        # 热退出缓存 + 会话恢复
    RecentFilesService.cs    # 最近打开列表
    ThemeService.cs          # 深/浅主题资源字典切换
    LocalizationService.cs   # 中英文资源切换
  Models/                    # 纯数据类型（Document、SearchResult、AppSettings 等）
  Theming/                   # Dark.xaml / Light.xaml 资源字典
  i18n/                      # zh-CN / en 资源
  plugins/                   # 输出目录，插件 dll 放这里
  config/settings.json       # 用户配置（首次运行生成）
  cache/                     # 热退出暂存数据
```

依赖方向：Views → ViewModels → Services → Models，反向用事件/接口。`ScintillaHost` 是唯一直接依赖 ScintillaNET 的类。

## 4. 关键设计决定

### 4.1 启动速度
- App 启动只做：单实例检查 → 加载配置 → 初始化主题/语言 → 显示主窗口。其余（插件扫描、会话恢复、文件树填充）全部在窗口显示后异步进行
- 发布使用 ReadyToRun（`PublishReadyToRun=true`）
- 不用单文件发布（自解压拖慢冷启动），用普通文件夹式便携包

### 4.2 ScintillaHost 封装
- 一个 `ScintillaHost` 用户控件包一个文档的编辑状态；每个 Tab 一个实例
- 主题切换时遍历所有实例重设 Scintilla 样式颜色
- 语法高亮：Lexer 名称 ↔ 扩展名映射表集中在 Models 中；DataWeave 例外——无 Lexilla 内置 Lexer，用 container lexer + `Services/DataWeaveLexer`（纯文本分词，不依赖 ScintillaNET）在 StyleNeeded 回调上色

### 4.3 线程模型
- 文件夹搜索、大文件读取、编码探测：后台 `Task`，通过 `IProgress<T>` 或 `Dispatcher` 回 UI
- 所有后台任务持有 `CancellationToken`，面板关闭/任务替换时取消旧任务

### 4.4 插件接口（Abstractions 内容）
- `ICodeFormatter`：`string Format(string input, FormatterOptions options)`，外加元数据属性（显示名、支持的语言标识 json/xml/html）
- 插件加载：`AssemblyLoadContext` 默认上下文 + `Assembly.LoadFrom`，逐个 try/catch，失败记入日志不中断

### 4.5 便携模式数据布局（exe 同目录）
- `config/settings.json`：全部用户设置
- `config/recent.json`：最近打开列表
- `cache/hotexit/`：未保存文档暂存（文件名做哈希映射，附元数据 json）
- `cache/session.json`：上次会话（打开的 Tab、文件夹、活动 Tab）

### 4.6 大文件加载
- `FileService` 提供 `IAsyncEnumerable<byte[]>` 分块读取（1MB/块）
- UI 层逐块 `AppendText`，期间 `ReadOnly=true`、Lexer=container/none，完成后设 Lexer 并 `Colourise`
- 进度经 `IProgress<int>` 报状态栏，取消经 `CancellationToken`

### 4.7 集成终端
- 三层结构：`ConPtySession`（ConPTY P/Invoke，零第三方依赖）→ `TerminalSession`（XtermSharp VT 解析/屏幕缓冲）→ `TerminalControl`（纯 WPF 自绘渲染）
- 底部可折叠面板独占编辑区一行（仿 FindBar 行模式），`Ctrl+\`` 切换，懒加载不影响冷启动
- 多 tab：`TerminalViewModel` 是 tab 容器（Tabs/ActiveTab/新建/关闭），每个 `TerminalTabViewModel` 持有独立 ConPTY 会话；`Ctrl+Shift+\`` 或 "+" 新建（用下拉框当前选中的 shell，下拉切换不重启已有会话）；关闭最后一个 tab 自动补空白 tab
- 视图层每个 tab 一个 `TerminalControl` 常驻、仅切 Visibility（同 ScintillaHost 模式），保住各自渲染状态/scrollback；`Session`/`FontSize` 是 DependencyProperty 供 DataTemplate 绑定
- scrollback 由渲染层自行实现（XtermSharp 官方无 scrollback），上限 5000 行
- 基础 shell 定位：不保证全屏交互程序（vim/htop）保真；隐藏面板保留全部会话，窗口关闭即终止所有进程

## 5. 构建与发布

- 开发：`dotnet build`（要求 0 警告，见 code-style.md §8）；根目录 `build.ps1` 一键构建/启动，`-Publish` 走发布
- 发布：`dotnet publish -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true`
