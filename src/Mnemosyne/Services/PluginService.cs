using System.IO;
using System.Reflection;
using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Services;

/// <summary>
/// 插件发现与加载（architecture.md 4.4）：扫描 exe 同目录 plugins/ 下的 dll，反射实例化
/// IMnemosynePlugin，注入 PluginContext 后按能力接口（ICodeFormatter 等）分别登记。
/// 单个 dll / 单个类型的任何异常都被隔离并记入 cache/plugin.log，不中断扫描、不影响主程序。
/// </summary>
public class PluginService
{
    private readonly List<IMnemosynePlugin> _plugins = [];
    private readonly List<ICodeFormatter> _formatters = [];
    private readonly Lock _gate = new();
    private readonly ConfigService _configService;
    private readonly string _pluginsDir;
    private readonly string _logPath;
    private volatile bool _scanned;

    public PluginService(ConfigService configService)
    {
        _configService = configService;
        _pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        _logPath = Path.Combine(AppContext.BaseDirectory, "cache", "plugin.log");
    }

    /// <summary>已加载的全部插件（设置页等界面展示用；未扫描时为空）</summary>
    public IReadOnlyList<IMnemosynePlugin> Plugins
    {
        get
        {
            lock (_gate)
            {
                return _plugins.ToArray();
            }
        }
    }

    /// <summary>后台扫描 plugins/ 目录（幂等，重复调用不重复扫描）</summary>
    public Task ScanAsync() => Task.Run(Scan);

    /// <summary>按语言标识查找格式化器（不区分大小写）；无匹配或尚未扫描到返回 null</summary>
    public ICodeFormatter? FindFormatter(string? languageId)
    {
        if (string.IsNullOrEmpty(languageId)) return null;
        lock (_gate)
        {
            return _formatters.FirstOrDefault(f =>
                f.LanguageIds.Any(id => string.Equals(id, languageId, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void Scan()
    {
        if (_scanned) return;
        try
        {
            if (Directory.Exists(_pluginsDir))
            {
                foreach (string dll in Directory.EnumerateFiles(_pluginsDir, "*.dll"))
                {
                    LoadAssembly(dll);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"扫描插件目录失败：{ex}");
        }
        _scanned = true;
    }

    private void LoadAssembly(string path)
    {
        string name = Path.GetFileName(path);
        // Abstractions 由主程序正常引用加载，跳过避免重复扫描
        if (name.Equals("Mnemosyne.Plugin.Abstractions.dll", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            Assembly assembly = Assembly.LoadFrom(path);
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // 部分类型依赖缺失时，仍加载可解析的类型
                types = ex.Types.OfType<Type>().ToArray();
                Log($"插件 {name} 存在无法解析的类型：{ex.LoaderExceptions.FirstOrDefault()?.Message}");
            }
            foreach (Type type in types)
            {
                TryCreatePlugin(type, name);
            }
        }
        catch (Exception ex)
        {
            Log($"加载插件 {name} 失败：{ex}");
        }
    }

    private void TryCreatePlugin(Type type, string assemblyName)
    {
        if (!typeof(IMnemosynePlugin).IsAssignableFrom(type)) return;
        if (type.IsAbstract || type.IsInterface || !(type.IsPublic || type.IsNestedPublic)) return;
        try
        {
            if (Activator.CreateInstance(type) is not IMnemosynePlugin plugin) return;
            PluginContext context = new(plugin, _configService, Log);
            plugin.Initialize(context);
            lock (_gate)
            {
                if (HasCapabilityConflict(plugin))
                {
                    Log($"插件 {assemblyName} 的 {type.FullName} 语言标识与已加载插件重复，已忽略");
                    return;
                }
                _plugins.Add(plugin);
                if (plugin is ICodeFormatter formatter) _formatters.Add(formatter);
            }
        }
        catch (Exception ex)
        {
            Log($"实例化插件类型 {type.FullName}（{assemblyName}）失败：{ex}");
        }
    }

    /// <summary>能力（格式化器）的语言标识与已登记插件重叠即视为冲突，调用方需持有 _gate</summary>
    private bool HasCapabilityConflict(IMnemosynePlugin plugin)
    {
        static bool Overlaps(IReadOnlyList<string> existing, IReadOnlyList<string> incoming) =>
            existing.Any(id => incoming.Any(newId =>
                string.Equals(id, newId, StringComparison.OrdinalIgnoreCase)));

        if (plugin is ICodeFormatter formatter
            && _formatters.Any(f => Overlaps(f.LanguageIds, formatter.LanguageIds)))
        {
            return true;
        }
        return false;
    }

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 日志失败不致命（便携目录可能只读）
        }
    }
}
