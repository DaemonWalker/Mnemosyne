# Mnemosyne 进度日志

> 本文件是跨会话恢复的唯一事实来源。规则见 `docs/steps.md` 顶部"进度记录协议"。
> 子 agent 每完成一个小目标必须立即追加日志并更新"当前状态"；中断前必须填写"断点信息"。

## 当前状态

- 当前 Step：**Step 3 已完成**（下一个待执行：Step 4 — 文件树）
- 当前 Step 内已完成的小目标：3.1～3.6 全部
- 最后更新：2026-09-04 00:47

## 断点信息

（无。中断恢复时由子 agent 填写：当前小目标编号、已完成的部分、下一步该做什么、遇到的问题）

## 日志

| 时间 | Step | 子目标 | 结果 | 构建 | 备注 |
|---|---|---|---|---|---|
| — | — | — | 项目初始化，文档就绪 | — | requirements/architecture/code-style/steps 已定稿 |
| 2026-09-03 20:59 | 1 | 1.1 | 解决方案与 5 个项目骨架建成（主程序 net10.0-windows，Abstractions/插件 netstandard2.0，Nullable+ImplicitUsings+LangVersion latest），引入 Scintilla5.NET 7.0.0 / Markdig 1.3.2 / UTF.Unknown 2.7.0 / CommunityToolkit.Mvvm 8.4.2；插件构建后自动拷贝 dll 到主程序输出 plugins/ | 0 警告 0 错误 | NuGet 包实际 ID 为 `UTF.Unknown`（architecture.md 写作 UtfUnknown） |
| 2026-09-03 21:07 | 1 | 1.2 | 启动骨架：`App.xaml.cs` 组合根 + `Views/MainWindow`；`SingleInstanceManager`（Mutex `Local\Mnemosyne.SingleInstance` + 命名管道 `Mnemosyne.SingleInstance.Pipe` 转发命令行参数并激活首实例窗口）；命令行路径解析后暂存 `App.PendingOpenPaths`（Step 3 消费） | 0 警告 0 错误 | 实测：主实例启动存活；再带参启动次实例，进程数保持 1 |
| 2026-09-03 21:07 | 1 | 1.3 | `ConfigService`（便携模式 exe 同目录 `config/settings.json`，System.Text.Json，损坏回退默认并覆写）+ `AppSettings`（主题/语言/字体/字号/缩进 Tab 与宽度/大文件阈值/自动换行/空白显示） | 0 警告 0 错误 | 实测：首启生成默认 settings.json；改 Theme/Language 后重启原样加载未被覆盖 |
| 2026-09-03 21:07 | 1 | 1.4 | `LocalizationService`（zh-CN/en，XAML 字符串资源字典 `i18n/Strings.*.xaml`，运行时换字典 + `LanguageChanged` 事件，XAML 经 DynamicResource 自动刷新，`GetString` 供代码取词） | 0 警告 0 错误 | 主窗口演示组合框切换语言即时生效并落盘 |
| 2026-09-03 21:07 | 1 | 1.5 | `Theming/Dark.xaml`/`Light.xaml`（颜色+画刷资源键）+ `ThemeService`（默认深色，换字典 + `ThemeChanged` 事件） | 0 警告 0 错误 | 主窗口演示组合框切换主题即时生效并落盘 |
| 2026-09-03 22:00 | 2 | 2.1 | 左侧活动栏：文件/搜索两个自绘 Path 图标 ToggleButton（`ActivityBarButtonStyle`，选中态高亮 + 左侧 accent 指示条），点击切换面板、再点收起（`MainWindowViewModel.ToggleActivityCommand`，`ActivityPanel` 枚举可空表示收起） | 0 警告 0 错误 | 新增 `Models/ActivityPanel.cs`、`ViewModels/MainWindowViewModel.cs` |
| 2026-09-03 22:00 | 2 | 2.2 | 侧边栏容器：`Views/FilePanelView`/`Views/SearchPanelView` 占位 UserControl（标题 + 空状态提示，均走 i18n）；列宽与 VM `SidebarWidth` 双向绑定，GridSplitter 可拖拽，收起时列宽置 0、展开恢复上次宽度 | 0 警告 0 错误 | — |
| 2026-09-03 22:00 | 2 | 2.3 | 中间 Tab 区：`Theming/Controls.xaml` 隐式 TabControl/TabItem 样式（标签栏底色、选中 Tab 顶部 accent 条、hover 态，画笔全 DynamicResource）；无文档时显示空状态提示（`ShowEmptyState`） | 0 警告 0 错误 | — |
| 2026-09-03 22:00 | 2 | 2.4 | 底部状态栏骨架：Ln/Col、UTF-8、CRLF、Plain Text、Spaces: 4 占位 + 右侧进度条预留位（Collapsed）；背景用 `Brush.StatusBar.Background`(=Accent) | 0 警告 0 错误 | — |
| 2026-09-03 22:00 | 2 | 2.5 | `Commands/AppCommands.cs`：需求 4.11 全部 9 个快捷键注册为 RoutedUICommand（KeyGesture 直接挂命令上，菜单自动显示快捷键文本）；主菜单（文件/编辑）+ Window CommandBindings；除 Ctrl+Shift+F 联动打开搜索侧边栏外均空实现 | 0 警告 0 错误 | 移除 Step 1 演示控件与 `Loc.Demo.*` 词条；实测 Dark/zh-CN 与 Light/en 两种配置窗口均正常启动；注意验证脚本须用 taskkill 清理进程，否则单实例会让后续启动直接退出 |
| 2026-09-04 00:47 | 3 | 3.1 | `Controls/ScintillaHost.cs`：ScintillaHost : WindowsFormsHost（csproj 加 UseWindowsForms + `<Using Remove="System.Windows.Forms"/>` 消歧义），字体/字号应用，`ApplyTheme()` 从主题资源键 `Color.Editor.*`（Dark/Light 各 18 个新键）取色，含约 25 个 Lexer 的配色表（cpp/python/xml/json/markdown/sql/powershell 等，部分用 SCE 数值常量）；行号边栏宽度随行数自适应；当前行高亮（v5 已废弃 CaretLineVisible，用 BackColor alpha=255）；缩进参考线；静态实例注册表 + `ApplyThemeToAll()` 供主题切换遍历 | 0 警告 0 错误 | PrintWindow 截图验证：行号/当前行/缩进线/配色全部生效 |
| 2026-09-04 00:47 | 3 | 3.2 | `Services/FileService.cs`（异步读写 + 编码探测）；`ViewModels/DocumentViewModel.cs`（标题/脏标记/路径/编码/行尾符/语言/行列号，持有每 Tab 一个的 ScintillaHost）；MainWindowViewModel 加 Documents/ActiveDocument + OpenPaths/Save/Close 命令；三入口：Ctrl+O OpenFileDialog、命令行 PendingOpenPaths（App.ArgsReceived 也转发 OpenPendingPaths）、窗口拖拽；同路径（OrdinalIgnoreCase）聚焦已有 Tab；关键设计：WindowsFormsHost 离开可视树会销毁原生句柄丢文档，故所有编辑器常驻 EditorHostGrid 只切 Visibility，TabControl 仅作标签条；Scintilla 聚焦时 WPF 收不到快捷键，由 EditorKeyDown 事件桥接匹配 AppCommands 手势 | 0 警告 0 错误 | 实测：命令行开 5 文件全成 Tab；二次启动同文件（单实例转发）聚焦已有 Tab、Tab 数不变；拖拽入口已实现未实测 |
| 2026-09-04 00:47 | 3 | 3.3 | 编码探测 BOM（UTF-8/16LE/BE/32）→ 严格 UTF-8 → UTF.Unknown（置信度>0.5）→ GB18030 兜底；App 启动注册 CodePagesEncodingProvider；`Models/EncodingCatalog.cs` 10 种可切换编码 + 显示名/同编码判断；状态栏编码按钮弹主题化 ContextMenu，切换即按新编码重载，脏文档先确认（ConfirmEncodingReload 钩子） | 0 警告 0 错误 | 实测：GBK 文件探测为 GB18030 不乱码；UTF-8 BOM/LF 文件显示正确；GB18030→GBK 手动切换重载正确（截图+UIA 验证） |
| 2026-09-04 00:47 | 3 | 3.4 | 保存 Ctrl+S（无路径走 SaveFilePicker 钩子）/另存为 Ctrl+Shift+S（新增 AppCommands.SaveAs + 菜单项）；脏标记为 Tab 标题圆点（SavePoint/TextChanged 驱动）；关闭脏 Tab 弹 保存/不保存/取消（ConfirmUnsavedClose 钩子，MessageBox YesNoCancel）；IO 异常统一 ReportError 本地化弹窗 | 0 警告 0 错误 | 实测：注入编辑 → 圆点出现；菜单保存 → 磁盘字节更新、BOM 保持、圆点消失；关闭脏 Tab → 弹出"文件"data.json"有未保存的修改"提示，选"不保存"后 Tab 关闭、磁盘不变。另存为对话框可正常弹出，应用侧逻辑与已验证的保存路径相同；本环境无法自动化完成系统保存对话框，人工点验留给后续走查 |
| 2026-09-04 00:47 | 3 | 3.5 | `Models/LineEnding.cs`；FileService.DetectLineEnding 按 CRLF/LF/CR 计数取主导（空文件默认 CRLF）；状态栏显示，点击弹 CRLF/LF 菜单，ConvertLineEnding 调 Scintilla ConvertEols 实际改内容 | 0 警告 0 错误 | 实测：LF 文件显示 LF；LF→CRLF 转换后脏标记出现、保存落盘字节为 \r\n |
| 2026-09-04 00:47 | 3 | 3.6 | `Models/LanguageRegistry.cs`：30 种语言定义（显示名/Lexer 名/扩展名/关键字表，cpp lexer 复用承载 C#/C/C++/Java/JS/TS）+ 扩展名索引 + CMakeLists.txt/Makefile/Dockerfile 文件名特例；`Views/LanguagePickerWindow`：Plain Text + 内置语言 + Lexilla 全部 Lexer（`ScintillaHost.GetAvailableLexerNames()`，首个控件创建前 Lexilla 静态委托未初始化需预热），搜索框过滤、Enter/双击/按钮确认；状态栏语言按钮打开弹窗 | 0 警告 0 错误 | 实测：.cs→C#、.c→C、.md→Markdown、.json→JSON 自动匹配正确（远超 10 种映射）；弹窗过滤 "py"→Python、选中后状态栏变更 |
