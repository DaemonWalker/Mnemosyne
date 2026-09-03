# Mnemosyne 文档索引

> 这是一个"类 Sublime Text 的轻量文本编辑器"项目（C# WPF + Scintilla，.NET 10）。
> **新对话的 AI 请先读完本目录全部文档再动手**，阅读顺序即下列顺序。

| 文档 | 内容 |
|---|---|
| [requirements.md](requirements.md) | 需求说明（唯一需求依据） |
| [architecture.md](architecture.md) | 技术架构与分层约定 |
| [code-style.md](code-style.md) | 代码规范（生成代码必须遵守） |
| [steps.md](steps.md) | 12 个实施 Step 的分解 + **进度记录协议** |
| [progress.md](progress.md) | 进度日志（跨会话恢复的事实来源） |

## 工作方式（给新对话的 AI）

1. 读 `progress.md` 的"当前状态"与"断点信息"，确定下一个要做的 Step 或小目标
2. 每个 Step 开一个子 agent 执行；子 agent 必须遵守 steps.md 顶部的进度记录协议（每完成一个小目标立即勾选 + 写日志）
3. 未经用户明确要求，不要偏离 requirements.md，不要做"明确不做"清单里的功能
