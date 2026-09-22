namespace Mnemosyne.Services;

/// <summary>
/// PluginService 登记好的插件主题：Name 为稳定标识（持久化到 settings.json），
/// XamlFilePath 为资源字典的绝对路径（PluginService 按插件 dll 目录解析）。
/// </summary>
public sealed record RegisteredTheme(string Name, string DisplayName, string XamlFilePath);
