namespace Mnemosyne.Plugin.Abstractions;

/// <summary>
/// 主程序注入给插件的宿主上下文，在 <see cref="IMnemosynePlugin.Initialize"/> 时传入。
/// </summary>
public interface IPluginContext
{
    /// <summary>当前插件的 <see cref="IMnemosynePlugin.Id"/>。</summary>
    string PluginId { get; }

    /// <summary>
    /// 插件私有数据目录（主程序已负责创建，便携模式下位于 exe 同目录 cache/plugins/&lt;插件 Id&gt;/）。
    /// 插件的缓存、下载物等应放在此目录内。
    /// </summary>
    string PluginDataDirectory { get; }

    /// <summary>
    /// 读取设置项当前生效值（用户在设置页改动后的值）。未设置过时返回该设置项
    /// <see cref="PluginSettingDescriptor.DefaultValue"/>；key 未声明时返回 null。
    /// 返回值类型与设置项类型对应：Bool→bool，Int→int，String/Enum/Path→string。
    /// 建议每次使用时现读，使用户改动即时生效。
    /// </summary>
    /// <param name="key">设置项 <see cref="PluginSettingDescriptor.Key"/>。</param>
    object? GetSetting(string key);

    /// <summary>写一行日志到主程序的插件日志（cache/plugin.log）。</summary>
    void Log(string message);
}
