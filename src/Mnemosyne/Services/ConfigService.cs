using System.IO;
using System.Text.Json;
using Mnemosyne.Models;

namespace Mnemosyne.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;

    public AppSettings Settings { get; private set; } = new();

    public ConfigService()
    {
        // 便携模式：配置目录固定在 exe 同目录
        string configDir = Path.Combine(AppContext.BaseDirectory, "config");
        Directory.CreateDirectory(configDir);
        _settingsPath = Path.Combine(configDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                string json = File.ReadAllText(_settingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                return Settings;
            }
            catch (Exception)
            {
                // 配置文件损坏时回退默认值并覆盖重写，保证可启动
            }
        }
        Settings = new AppSettings();
        Save();
        return Settings;
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(Settings, SerializerOptions);
        File.WriteAllText(_settingsPath, json);
    }

    /// <summary>
    /// 读插件设置值。未存储过或类型无法转换时返回 defaultValue。
    /// </summary>
    public object? GetPluginSetting(string pluginId, string key, object? defaultValue)
    {
        if (!Settings.PluginSettings.TryGetValue(pluginId, out Dictionary<string, JsonElement>? values)
            || !values.TryGetValue(key, out JsonElement element))
        {
            return defaultValue;
        }
        try
        {
            return defaultValue switch
            {
                bool => element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False
                    ? element.GetBoolean()
                    : defaultValue,
                int => element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int i)
                    ? i
                    : defaultValue,
                _ => element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : defaultValue,
            };
        }
        catch (Exception)
        {
            return defaultValue;
        }
    }

    /// <summary>写插件设置值（bool/int/string），不立即落盘，随下次 Save 批量写入。</summary>
    public void SetPluginSetting(string pluginId, string key, object? value)
    {
        if (!Settings.PluginSettings.TryGetValue(pluginId, out Dictionary<string, JsonElement>? values))
        {
            values = new Dictionary<string, JsonElement>();
            Settings.PluginSettings[pluginId] = values;
        }
        JsonElement element = value switch
        {
            bool b => JsonSerializer.SerializeToElement(b),
            int i => JsonSerializer.SerializeToElement(i),
            _ => JsonSerializer.SerializeToElement(value?.ToString() ?? string.Empty),
        };
        values[key] = element;
    }
}
