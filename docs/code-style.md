# Mnemosyne 代码规范

> 所有子 agent 生成的代码必须遵守本规范。与规范冲突的现有代码，在改动到该文件时顺带修正。

## 1. 项目与语言设置

- 所有项目启用：`<Nullable>enable</Nullable>`、`<ImplicitUsings>enable</ImplicitUsings>`、`<LangVersion>latest</LangVersion>`
- C# 文件使用**文件级命名空间**（`namespace Mnemosyne.Services;`）
- 目标框架见 architecture.md；不要在未经确认的情况下新增 NuGet 依赖

## 2. 命名与组织

- 一个文件一个公共类型，文件名 = 类型名
- 目录按 architecture.md 的分层放（Views/ViewModels/Services/Models/Controls），不按类型拼音或随意新建目录
- 命名遵循标准 .NET 约定：类型/方法/属性 PascalCase，私有字段 `_camelCase`，参数/局部变量 camelCase，接口 `I` 前缀
- async 方法以 `Async` 结尾；返回 `Task`/`ValueTask`，不写 `async void`（事件处理器除外）
- XAML 控件命名用 `x:Name="PascalCase"`，仅当 code-behind 或绑定确实引用时才命名

## 3. MVVM 约定

- ViewModel 使用 CommunityToolkit.Mvvm 源生成器：`[ObservableProperty]`（字段 `_camelCase`）与 `[RelayCommand]`
- View 的 code-behind 只处理纯 UI 逻辑（焦点、拖拽视觉、Scintilla 事件转发）；业务逻辑放 ViewModel/Service
- Service 为无状态或可注入的单例，用构造函数注入；不引入额外 DI 容器，在 `App.xaml.cs` 手写组合根（composition root）

## 4. 注释与文档

- 默认**不写注释**；仅在逻辑非显而易见（变通方案、内核限制规避、非直觉的 API 用法）时写简短中文注释说明"为什么"
- 公共插件接口（Abstractions 项目）写 XML 文档注释（中文），因为这是插件作者的契约
- 禁止保留被注释掉的代码

## 5. UI 与资源

- 界面可见文本一律走 i18n 资源，**禁止在 XAML/C# 写死中文或英文**
- 颜色/画刷一律引用主题资源键（`DynamicResource`），禁止硬编码色值
- 样式集中在 `Theming/` 与窗口的 `Resources`，避免大量内联 Style

## 6. 错误处理

- 后台任务必须捕获异常并转化为用户可见的状态栏提示或对话框，禁止静默吞掉
- 插件加载/执行逐个 try/catch，单个插件失败不影响主程序
- 文件 IO 全部假设会失败（占用、权限、路径不存在），给出中文/英文本地化错误提示

## 7. 线程与取消

- 超过 50ms 的工作不放 UI 线程
- 长任务必须接受 `CancellationToken` 并响应取消
- 跨线程更新 UI 用 `Dispatcher` 或 `IProgress<T>`，禁止直接触碰 UI 元素

## 8. 验收底线（每个小目标完成时）

- `dotnet build` 整个解决方案通过，**0 警告**（含 nullable 警告）
- 该小目标涉及的 steps.md 验收条目实际验证通过
- 按 steps.md 的进度记录协议更新 `docs/steps.md` 复选框与 `docs/progress.md`
