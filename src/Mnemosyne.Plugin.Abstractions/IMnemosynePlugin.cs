namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// Mnemosyne 插件身份契约。主程序扫描 exe 同目录 plugins/ 下的 dll，
/// 实例化所有实现本接口的公共非抽象类（需要有无参构造函数），随后调用
/// <see cref="Initialize"/> 注入宿主上下文。
/// 一个插件类可同时实现若干能力接口（如 <see cref="ICodeFormatter"/>），
/// 主程序按其具备的能力分别登记。
/// </summary>
public interface IMnemosynePlugin
{
    /// <summary>
    /// 插件稳定标识（如 "mnemosyne.formatters.json"）。一经发布不得更改：
    /// 用作设置存储键、私有数据目录名和日志标识。建议反向域名风格，全小写。
    /// </summary>
    string Id { get; }

    /// <summary>插件显示名（用于设置页、菜单、错误提示等界面展示）。</summary>
    string DisplayName { get; }

    /// <summary>插件版本号（展示用，如 "1.0.0"）。</summary>
    string Version { get; }

    /// <summary>插件功能简述（设置页展示）。</summary>
    string Description { get; }

    /// <summary>
    /// 插件声明的设置项列表（无设置时返回空数组）。插件只描述 schema，
    /// 设置界面由主程序渲染，值经 <see cref="IPluginContext.GetSetting"/> 读取。
    /// </summary>
    IReadOnlyList<PluginSettingDescriptor> Settings { get; }

    /// <summary>
    /// 初始化回调：主程序在实例化后调用一次。实现应只做轻量工作
    /// （保存 context 引用等），耗时初始化推迟到能力被实际使用时进行。
    /// 抛异常会导致该插件被整体忽略并记入日志。
    /// </summary>
    /// <param name="context">宿主上下文（设置读取、私有数据目录、日志）。</param>
    void Initialize(IPluginContext context);
}
