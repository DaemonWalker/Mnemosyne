# Mnemosyne 文档索引

> 这是一个"类 Sublime Text 的轻量文本编辑器"项目（C# WPF + Scintilla，.NET 10），功能已全部实现并趋近稳定。
> **新对话的 AI 请先读完本目录全部文档再动手**，阅读顺序即下列顺序。

| 文档 | 内容 |
|---|---|
| [requirements.md](requirements.md) | 需求说明（唯一需求依据，含"明确不做"清单） |
| [architecture.md](architecture.md) | 技术架构与分层约定 |
| [code-style.md](code-style.md) | 代码规范（生成代码必须遵守）与验收底线 |
| [notes.md](notes.md) | 实现笔记：Scintilla/WPF 踩坑、关键设计决策、验证环境要点 |

## 工作方式（给新对话的 AI）

1. 改动前先读上述四篇文档；未经用户明确要求，不要偏离 requirements.md，不要做"明确不做"清单里的功能
2. 每处改动按 code-style.md §8 验收：构建 0 警告 + 相关功能实际验证通过；核心链路改动跑 `scripts/regression-smoke.ps1` 冒烟
3. 常用命令：根目录 `build.ps1`（开发构建，`-Run` 启动）、`build.ps1 -Publish`（发布到 `artifacts/publish/`）
4. 发现新的内核坑或关键决策，补记到 `docs/notes.md`
