# Mnemosyne

类 Sublime Text 的轻量 Windows 文本编辑器。核心指标：**冷启动 1 秒内**（普通 SSD，实测约 0.5 秒）、内存占用小、便携免安装。

- UI：C# + WPF（.NET 10，`net10.0-windows`）
- 编辑内核：Scintilla v5（Scintilla5.NET）
- MVVM：CommunityToolkit.Mvvm（源生成器）

## 功能

- **打开**：单文件 / 文件夹（侧边栏文件树）/ 拖拽 / 命令行，单实例转发，最近打开列表，可选 Windows 右键菜单集成
- **编辑**：多光标（Ctrl+点击、Alt+拖拽列选择、Ctrl+D）、缩进自动检测、CRLF/LF 显示与转换、自动换行、空白字符可视化、代码折叠
- **搜索**：页内查找替换（大小写 / 全字 / 正则，可组合）；文件夹内全文搜索（glob 包含/排除、后台按需扫描、可取消、结果增量显示）
- **语法高亮**：Scintilla 全部 Lexer + 自研 DataWeave 词法器（Lexilla 未覆盖），按扩展名自动匹配，弹窗搜索切换语言
- **编码**：UTF-8 优先 + BOM + UDE 自动探测（GBK 等），状态栏手动切换重载
- **Markdown**：编辑高亮 + 预览新 Tab（Markdig → WPF 原生渲染，不用 WebView2），保存后自动刷新
- **格式化插件**：约定接口 + 反射加载 `plugins/` 目录，内置 JSON / XML / HTML 格式化器
- **集成终端**：底部可折叠面板（Ctrl+`），ConPTY 后端 + 自绘渲染，默认 PowerShell 可在设置中切换 cmd/pwsh，配色跟随主题
- **大文件**：超阈值（默认 50MB 可调）进入分块边读边显模式，带进度与取消
- **热退出**：退出不提示保存，未保存修改暂存本地，重启自动恢复；会话记住 Tab / 文件夹 / 光标；外部修改提示重载
- **主题与语言**：深 / 浅双主题，中 / 英双语界面，设置中即时切换

明确不做：连体字、Minimap、分栏、文件夹级替换、后台索引、定时自动保存、网络更新、命令面板、打印、拼写检查、宏、Git；终端内搜索 / 超链接点击 / 会话持久化亦不做。

## 运行环境

- Windows 10+，x64
- 开发构建：.NET 10 SDK
- 发布包为 framework-dependent + ReadyToRun，目标机器需安装 .NET 10 桌面运行时

## 构建与发布

```powershell
# 开发构建（0 错误 0 警告基线），加 -Run 构建后启动
.\build.ps1 [-Run]

# 发布便携包到 artifacts/publish/（Release + ReadyToRun）
.\build.ps1 -Publish
```

发布包解压即用，便携模式数据全部在 exe 同目录：

```
config/settings.json   # 用户设置
config/recent.json     # 最近打开
cache/hotexit/         # 未保存修改暂存
cache/session.json     # 会话
cache/error.log        # 崩溃日志
plugins/               # 插件 dll（放这里即被加载）
```

## 仓库结构

```
src/Mnemosyne/                      # WPF 主程序
src/Mnemosyne.Plugin.Abstractions/  # 插件接口（netstandard2.0）
plugins/                            # 内置格式化器（Json / Xml / Html）
docs/                               # 需求 / 架构 / 代码规范 / 实现笔记
scripts/                            # 回归冒烟、冷启动测量、图标生成
build.ps1                           # 一键构建 / 发布
```

## 快捷键

| 快捷键 | 功能 |
|---|---|
| Ctrl+N / Ctrl+O / Ctrl+Shift+O | 新建 / 打开文件 / 打开文件夹 |
| Ctrl+S / Ctrl+Shift+S | 保存 / 另存为 |
| Ctrl+F / Ctrl+H / Ctrl+Shift+F | 页内搜索 / 替换 / 文件夹搜索 |
| Ctrl+W | 关闭 Tab |
| Ctrl+D | 选中下一个相同词（多光标） |
| Ctrl+` | 终端面板开合 |
| Ctrl+Shift+V | Markdown 预览 |
| Ctrl+, | 设置 |

## 文档

开发相关文档见 [docs/](docs/README.md)：需求说明、技术架构、代码规范、实现笔记（Scintilla/WPF 踩坑记录）。
