# Mnemosyne 进度日志

> 本文件是跨会话恢复的唯一事实来源。规则见 `docs/steps.md` 顶部"进度记录协议"。
> 子 agent 每完成一个小目标必须立即追加日志并更新"当前状态"；中断前必须填写"断点信息"。

## 当前状态

- 当前 Step：**Step 1 已完成**（下一个待执行：Step 2 — 主窗口布局）
- 当前 Step 内已完成的小目标：1.1～1.5 全部
- 最后更新：2026-09-03 21:07

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
