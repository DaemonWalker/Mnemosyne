using System.Text.Json;

namespace Mnemosyne.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "zh-CN";
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 13;
    public bool IndentUseTabs { get; set; }
    public int IndentWidth { get; set; } = 4;
    public int LargeFileThresholdMB { get; set; } = 50;
    public bool WordWrap { get; set; }
    public bool ShowWhitespace { get; set; }
    public bool HideDotFiles { get; set; } = true;
    public bool HideHiddenFiles { get; set; } = true;
    public string UiFontFamily { get; set; } = "";
    public double UiFontSize { get; set; } = 12;
    public bool AllowMultipleInstances { get; set; }
    public bool ShellFileContextMenu { get; set; }
    public bool ShellFolderContextMenu { get; set; }
    public string TerminalShell { get; set; } = "powershell";

    /// <summary>插件设置：插件 Id → (设置键 → 值)。键值结构由各插件的 PluginSettingDescriptor 声明。</summary>
    public Dictionary<string, Dictionary<string, JsonElement>> PluginSettings { get; set; } = new();

    /// <summary>浅克隆：PluginSettings 字典引用共享，保存时整棵替换写回（与设置页批量保存语义一致）。</summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
