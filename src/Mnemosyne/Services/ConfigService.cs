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
}
