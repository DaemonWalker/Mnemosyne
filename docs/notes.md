# Mnemosyne 实现笔记（踩坑与关键决策）

> 从开发日志中提炼的长期有效知识。发现新的内核坑、环境坑或非直觉决策时补记于此。

## 1. Scintilla5.NET 7.0 的坑

- **position 单位是 .NET 字符索引，不是 UTF-8 字节偏移**（实测 TargetText/TextLength 证实）：选中/高亮/替换直接用字符索引，无需编码换算
- 弹窗方法名是 `UsePopup`（非 `UsePopUp`）；已用 `UsePopup(Never)` 关闭内置英文右键菜单，改由 `EditorRightClick` 事件弹本地化 WPF ContextMenu
- 无 `UndoCollection` 封装，关撤销用 `DirectMessage` 调 `SCI_SETUNDOCOLLECTION(2012)`；无 `Colourise` 只有 `Colorize`
- `CaretLineVisible` 在 v5 已废弃，当前行高亮用 BackColor alpha=255；`CaretLineBackColorAlpha` 属性已 Obsolete（选中文本时取消当前行高亮是改 UpdateUI 里按选区切 BackColor alpha 0/255）
- **代码折叠**：fold 属性 + margin1 + AutomaticFold 对 22 种语言生效，但 **Markdown/Batch/Makefile 的 Lexer 实测不支持折叠**（以 Lexilla 5.5.0 为准）；大文件模式关闭折叠
- Lexilla 静态委托在首个控件创建前未初始化，枚举全部 Lexer 前需预热（`ScintillaHost.GetAvailableLexerNames()`）
- cpp lexer 复用承载 C#/C/C++/Java/JS/TS
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
- netstandard2.0 插件：无 Span/char 重载 EndsWith、无 record struct（IsExternalInit）；JSON 插件引 System.Text.Json 8.0.5 仅编译用，运行时由主程序 net10 高版本承载，dll 不拷出
- XML 声明须手工按原文 version/encoding/standalone 重写（XmlWriter 走 StringBuilder 会把 encoding 写成 utf-16）

## 7. 单实例与壳集成

- 单实例：Mutex `Local\Mnemosyne.SingleInstance` + 命名管道转发命令行参数并激活首实例；`AllowMultipleInstances=true` 时完全不创建 SingleInstanceManager；ConfigService.Load 必须先于单实例检查
- 右键菜单注册表（HKCU，无需管理员）：文件 `Software\Classes\*\shell\Mnemosyne`（`"%1"`）、文件夹 `Directory\shell\Mnemosyne` **和** `Directory\Background\shell\Mnemosyne`（均 `"%V"`）——漏了 Background 键会导致在文件夹窗口空白处右键没有菜单

## 8. 构建 / 发布 / 脚本环境

- 根目录 `build.ps1`：默认 Debug 开发构建（`-warnaserror`、`-Run`）；`-Publish` 走 Release + R2R 发布到 `artifacts/publish/`（拷插件 dll 到 plugins/、建 config/ cache/）
- **PS 5.1 脚本含中文必须 UTF-8 BOM**（无 BOM 按 GBK/ANSI 解析直接语法错误或乱码）；Edit 工具重写会丢 BOM，需重补
- `dotnet publish --no-build -r win-x64` 报 NETSDK1047（还原时未带 RID），去掉 `--no-build` 即可
- 应用图标：16~64 用 BMP 帧、128/256 用 PNG 帧——System.Drawing.Icon 对 PNG 帧解码有 bug，纯 PNG ico 会炸；生成脚本 `scripts/generate-icon.ps1`
- 冷启动实测（NVMe SSD，发布包）：无会话 avg≈480ms，有会话 avg≈520ms，测量脚本 `scripts/measure-coldstart.ps1`
- PowerShell 访问含 `*` 的注册表路径必须用 `-LiteralPath`，否则按通配符枚举整个 Classes hive 卡死
- 崩溃日志：`Services/CrashLogger.cs` → `cache/error.log`（1MB 轮转，三全局钩子只记不改崩溃语义）

## 9. 自动化验证环境要点（本机）

- 跨进程**绝不能给 SCI_GETTEXTRANGE/SCI_GETSELTEXT 类消息传本进程缓冲区**（写坏目标进程内存致崩溃）；SCI_SETTEXT 同理勿用，文本注入用 SendMessage WM_CHAR 到 Scintilla 原生窗口（类名 `WindowsForms10.Scintilla.*`，非 "Scintilla"）
- 单实例应用验证前必须 taskkill 清场，否则后续启动直接退出
- keybd_event 注入在本环境常被吞；模态对话框自动化被禁止，相关弹窗靠人工点验
- 锁屏 Backstop 窗口存在时：SendMessage 注入 WPF 元素无效，真实输入（SetCursorPos+mouse_event）可用但需重试；截图用 PrintWindow(PW_RENDERFULLCONTENT)，CopyFromScreen 在全屏游戏占用时截不到
