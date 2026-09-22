# Mnemosyne 实现笔记（踩坑与关键决策）

> 从开发日志中提炼的长期有效知识。发现新的内核坑、环境坑或非直觉决策时补记于此。

## 1. Scintilla5.NET 7.0 的坑

- **position 单位是 .NET 字符索引，不是 UTF-8 字节偏移**（实测 TargetText/TextLength 证实）：选中/高亮/替换直接用字符索引，无需编码换算
- 弹窗方法名是 `UsePopup`（非 `UsePopUp`）；已用 `UsePopup(Never)` 关闭内置英文右键菜单，改由 `EditorRightClick` 事件弹本地化 WPF ContextMenu
- 无 `UndoCollection` 封装，关撤销用 `DirectMessage` 调 `SCI_SETUNDOCOLLECTION(2012)`；无 `Colourise` 只有 `Colorize`
- `CaretLineVisible` 在 v5 已废弃，当前行高亮用 BackColor alpha=255；`CaretLineBackColorAlpha` 属性已 Obsolete（选中文本时取消当前行高亮是改 UpdateUI 里按选区切 BackColor alpha 0/255）
- **代码折叠**：fold 属性 + margin1 + AutomaticFold 对 37 种语言生效，但 **Markdown/Batch/Makefile 的 Lexer 实测不支持折叠**（以 Lexilla 5.5.0 为准）；大文件模式关闭折叠
- Lexilla 静态委托在首个控件创建前未初始化，枚举全部 Lexer 前需预热（`ScintillaHost.GetAvailableLexerNames()`）
- cpp lexer 复用承载 C#/C/C++/Java/JS/TS，以及 Go/Kotlin/Swift/Objective-C/Groovy/Scala——**Lexilla（5.5.0）没有这 6 种语言的独立 Lexer**（实测 `Lexilla.GetLexerNames()` 确认），靠 cpp + 各自关键字表区分
- **`Colorize(0, -1)` 在 7.0.0 实测不生效**（GetEndStyled 不前进），全文着色必须传显式长度 `Colorize(0, TextLength)`
- **DataWeave（.dwl）走 container lexer**（Lexilla 无内置）：container lexer **不能用 `LexerName = "container"` 设置**——Scintilla5.NET 7.0 的 LexerName 走 Lexilla.CreateLexer，无此名会抛异常被兜底成 null lexer，高亮静默失效（曾长期未被发现，冒烟未覆盖 .dwl）。正确做法是 `DirectMessage(SCI_SETILEXER=4033, 0, 0)` 传空指针。分词器已外迁为插件（`plugins/Mnemosyne.Languages.DataWeave`，实现 ICustomLexer），宿主经 CustomLexerRegistry 通用化处理任何插件词法器。StyleNeeded 回调里分词、`StartStyling`/`SetStyling` 上色；从已着色末尾回溯到行首起扫，跨行块注释/未闭合字符串在中间起扫时可能短暂错色，属固有取舍。分词规则参照官方语法 mulesoft/data-weave-tmLanguage
- 光标在行缩进区内按 Tab 是"缩进整行"（内建行为，验证时注意）

## 2. WPF / WindowsFormsHost 结构约束

- **空域（airspace）限制：WPF 浮层无法覆盖 WindowsFormsHost**（被 Scintilla 遮挡）。搜索条因此不是浮层，而是编辑器区顶部内嵌一整行（收起时高度 0）；Markdown 预览 Tab 为纯 WPF 内容，不受此限
- **WindowsFormsHost 离开可视树会销毁原生句柄丢文档**：所有编辑器常驻 `EditorHostGrid` 只切 `Visibility`，TabControl 仅作标签条；MainWindow 用 `_tabContents` 字典管理常驻内容
- Scintilla 聚焦时 WPF 收不到快捷键，由 `EditorKeyDown` 事件桥接匹配 AppCommands 手势
- 窗口按钮无文字 Content 时，依赖 `AutomationProperties.Name` 供 UIA 识别
- 自定义 WindowChrome 最大化时整体外扩 ResizeBorderThickness(6px)，需补偿根 Grid `Margin=6`、还原清零
- ComboBox 自定义模板缺 `ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"` 会让选中框显示 record 的 ToString；ToggleButton 模板不绑背景会导致深色主题下异常
- `Run.Text` 默认 TwoWay 绑定只读属性会抛 XamlParseException，须 `Mode=OneWay`；HierarchicalDataTemplate 必须显式 `ItemTemplate` 指定子项模板
- System.Drawing/System.Windows.Documents 全局 using 与 Markdig 类型撞名，用 `Md*` 别名消歧

## 3. 热退出 / 会话恢复

- **暂存必须显式清除**（保存成功/Tab 移除/另存为迁移键三处），不能挂在 dirty→clean 转换上——打开文件加载会经过干净态，会把待恢复暂存误删
- `EmptyUndoBuffer` 会重置保存点使恢复文档显示为未修改；规避：置文本+清撤销后做一次"插入再删除"净零编辑（单撤销动作）让 Modified 成立
- Markdown 预览 Tab 不进会话
- 同一文件并发打开去重：去重检查与 `Documents.Add` 之间隔着异步加载会被穿透，用 `_openingDocuments` 字典登记在途 Task
- 多实例各自整体覆写 `recent.json` 会互相覆盖：`Save()` 写盘前重读磁盘并合并（内存优先、OrdinalIgnoreCase 去重）

## 4. 大文件分块加载

- `FileService.ReadChunksAsync`：1MB/块 + `Decoder` 跨块保状态，不完整多字节序列顺延下一块（UTF-8/GBK/UTF-16 统一）；BOM 不跳过，解码为 U+FEFF
- **SCI_APPENDTEXT 受 ReadOnly 阻断**，AppendChunk 时需临时解除只读
- 编码探测读头 64KB 样本，严格 UTF-8 先按 UTF-8 边界对齐样本尾避免截断误判
- 探测顺序：BOM → 严格 UTF-8 → UTF.Unknown（置信度>0.5）→ GB18030 兜底；App 启动须注册 `CodePagesEncodingProvider`

## 5. 搜索实现要点

- 三选项统一走 .NET Regex（字面量模式 Regex.Escape；Multiline 让 ^/$ 按行边界）
- **全字匹配不用 `\b`**：词字符限定 ASCII 字母数字下划线，中文字符非词字符故中文词前后总是边界（"中文没有词边界"语义）
- 文件夹搜索按行匹配，不支持跨行正则（与结果展示模型一致）
- glob 匹配见 `Services/GlobMatcher.cs`（VSCode 风格）；默认排除 .git/bin/obj/node_modules，跳过 ReparsePoint 防目录循环；二进制嗅探=头部 NUL 字节（带 BOM 的 UTF-16/32 豁免）

## 6. 插件系统

- 约定：Format 返回行尾符为 `\n`，由主程序按文档行尾符归一化；只有 Format 成功才替换原文
- netstandard2.0 插件：无 Span/char 重载 EndsWith、无 record struct（IsExternalInit）、string 无 Range 索引器（用 Substring）；JSON 插件引 System.Text.Json 8.0.5 仅编译用，运行时由主程序 net10 高版本承载，dll 不拷出
- XML 声明须手工按原文 version/encoding/standalone 重写（XmlWriter 走 StringBuilder 会把 encoding 写成 utf-16）
- 能力接口现有四种：ICodeFormatter / ILanguageContribution / ICustomLexer / IThemeContribution；冲突规则——格式化器语言标识与词法器名冲突整个插件忽略，语言扩展名/文件名与主题名冲突只跳过冲突条目，均记 plugin.log
- 语言全由插件贡献：宿主 LanguageRegistry 只剩 Plain Text，扫描结束一次性 RegisterLanguages（快照整体替换，读侧无锁）；**启动必须先 ScanAsync 再 RestoreSessionAsync**，否则恢复的文件按纯文本打开
- 自定义词法器走 container lexer：插件声明 Styles（语义 + Bold），Tokenize 产出（Length, 样式下标）；宿主按语义映射 Color.Editor.* 主题色，样式下标越界钳到样式表末尾
- 主题插件：xaml 资源字典随 dll 放 plugins/，宿主 file URI 加载并先压入 Dark 字典兜底缺键（否则 EditorColor 缺键返回 Magenta）；**Brush.* 在字典内 StaticResource 解析，主题包必须自带 Brush 定义**（以 Dark.xaml 全文为模板）；发布时 build.ps1 会一并拷 plugins/*.xaml

## 7. 单实例与壳集成

- 单实例：Mutex `Local\Mnemosyne.SingleInstance` + 命名管道转发命令行参数并激活首实例；`AllowMultipleInstances=true` 时完全不创建 SingleInstanceManager；ConfigService.Load 必须先于单实例检查
- 右键菜单注册表（HKCU，无需管理员）：文件 `Software\Classes\*\shell\Mnemosyne`（`"%1"`）、文件夹 `Directory\shell\Mnemosyne` **和** `Directory\Background\shell\Mnemosyne`（均 `"%V"`）——漏了 Background 键会导致在文件夹窗口空白处右键没有菜单

## 8. 集成终端

- **`UpdateProcThreadAttribute` 的导出名不带 "List"**（InitializeProcThreadAttributeList 才带），写错会 EntryPointNotFoundException，须用 `EntryPoint=` 显式指定
- **XtermSharp 内建 scrollback**（`TerminalOptions.Scrollback`，默认 1000，本项目设 5000）：`Buffer.Lines` 为含滚出历史的 CircularList，`YBase`/`YDisp` 定位可视区，`ScrollLines(n)` 响应滚轮——无需渲染层自行实现（官方 README 说缺失已过时）
- XtermSharp 属性位无公开解码 API（从源码确认）：`bg = attr & 0x1ff`、`fg = (attr>>9) & 0x1ff`、`flags = attr>>18`；颜色值 256=默认、257=反色默认、0–255 查 `Color.DefaultAnsiColors`
- **ConPTY 子进程 std 句柄继承父进程**：GUI 方式启动正常；若从带输出重定向的终端里启动（如 `dotnet run | ...`），cmd 的 banner/提示符会旁路到父进程管道而不经 ConPTY——CreateProcess 固有语义（微软官方示例同行为）
- 项目启用 UseWindowsForms 带来全局 `System.Drawing`，且 `Mnemosyne.Services.Terminal` 命名空间遮蔽 `XtermSharp.Terminal`，均需 using 别名消解
- NuGet 加 XtermSharp 必须显式带版本号（`--version 1.0.0-alpha.10`），裸 `dotnet add` 报"没有可用版本"
- **WPF 绑定在 DataBind 优先级执行，晚于 Render**：终端面板最初用绑定挂 DataContext 和行高，实测偶发（约一半概率）"面板展开但会话不启动、ComboBox 丢选中"——Render 阶段的尺寸/渲染事件先于绑定求值。修复：DataContext 与行高改由 `ToggleTerminalCommand_Executed` 命令式设置；ComboBox 候选改 XAML 静态项（不用 ItemsSource 绑定，消除与 SelectedItem 的求值顺序竞态）；`DataContextChanged` 里补调一次 `EnsureSession` 兜底
- ComboBox TwoWay SelectedItem 绑定在项集合刷新瞬间会回推 null，`OnSelectedShellChanged` 需忽略空值并还原，否则选中框被清空

## 9. 构建 / 发布 / 脚本环境

- 根目录 `build.ps1`：默认 Debug 开发构建（`-warnaserror`、`-Run`）；`-Publish` 走 Release + R2R 发布到 `artifacts/publish/`（拷插件 dll 到 plugins/、建 config/ cache/）
- **PS 5.1 脚本含中文必须 UTF-8 BOM**（无 BOM 按 GBK/ANSI 解析直接语法错误或乱码）；Edit 工具重写会丢 BOM，需重补
- `dotnet publish --no-build -r win-x64` 报 NETSDK1047（还原时未带 RID），去掉 `--no-build` 即可
- 应用图标：16~64 用 BMP 帧、128/256 用 PNG 帧——System.Drawing.Icon 对 PNG 帧解码有 bug，纯 PNG ico 会炸；生成脚本 `scripts/generate-icon.ps1`
- 冷启动实测（NVMe SSD，发布包）：无会话 avg≈480ms，有会话 avg≈520ms，测量脚本 `scripts/measure-coldstart.ps1`
- PowerShell 访问含 `*` 的注册表路径必须用 `-LiteralPath`，否则按通配符枚举整个 Classes hive 卡死
- 崩溃日志：`Services/CrashLogger.cs` → `cache/error.log`（1MB 轮转，三全局钩子只记不改崩溃语义）

## 10. 自动化验证环境要点（本机）

- 跨进程**绝不能给 SCI_GETTEXTRANGE/SCI_GETSELTEXT 类消息传本进程缓冲区**（写坏目标进程内存致崩溃）；SCI_SETTEXT 同理勿用，文本注入用 SendMessage WM_CHAR 到 Scintilla 原生窗口（类名 `WindowsForms10.Scintilla.*`，非 "Scintilla"）
- 单实例应用验证前必须 taskkill 清场，否则后续启动直接退出
- keybd_event 注入在本环境常被吞；模态对话框自动化被禁止，相关弹窗靠人工点验
- 锁屏 Backstop 窗口存在时：SendMessage 注入 WPF 元素无效，真实输入（SetCursorPos+mouse_event）可用但需重试；截图用 PrintWindow(PW_RENDERFULLCONTENT)，CopyFromScreen 在全屏游戏占用时截不到
