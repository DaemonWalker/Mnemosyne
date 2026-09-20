using System.IO;
using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Services;

/// <summary>
/// IPluginContext 的主程序实现：按插件声明的 descriptor 提供默认值回落的设置读取、
/// 私有数据目录（cache/plugins/&lt;插件 Id&gt;/）与插件日志。
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly ConfigService _configService;
    private readonly IReadOnlyDictionary<string, PluginSettingDescriptor> _descriptors;
    private readonly Action<string> _log;

    public PluginContext(IMnemosynePlugin plugin, ConfigService configService, Action<string> log)
    {
        PluginId = plugin.Id;
        _configService = configService;
        _descriptors = plugin.Settings.ToDictionary(d => d.Key, StringComparer.Ordinal);
        _log = log;
        string dir = Path.Combine(AppContext.BaseDirectory, "cache", "plugins", plugin.Id);
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception)
        {
            // 便携目录只读时目录可能创建失败；插件自行容错（GetSetting/Log 仍可用）
        }
        PluginDataDirectory = dir;
    }

    public string PluginId { get; }

    public string PluginDataDirectory { get; }

    public object? GetSetting(string key)
    {
        if (!_descriptors.TryGetValue(key, out PluginSettingDescriptor? descriptor))
        {
            return null;
        }
        return _configService.GetPluginSetting(PluginId, key, descriptor.DefaultValue);
    }

    public void Log(string message) => _log($"[{PluginId}] {message}");
}
