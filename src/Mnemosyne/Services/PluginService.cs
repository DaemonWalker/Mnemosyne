using System.IO;
using System.Reflection;
using Mnemosyne.Plugin.Abstractions;

namespace Mnemosyne.Services;

/// <summary>
/// 插件发现与加载（architecture.md 4.4）：扫描 exe 同目录 plugins/ 下的 dll，反射实例化 ICodeFormatter。
/// 单个 dll / 单个类型的任何异常都被隔离并记入 cache/plugin.log，不中断扫描、不影响主程序。
/// </summary>
public class PluginService
{
    private readonly List<ICodeFormatter> _formatters = [];
    private readonly Lock _gate = new();
    private readonly string _pluginsDir;
    private readonly string _logPath;
    private volatile bool _scanned;

    public PluginService()
    {
        _pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        _logPath = Path.Combine(AppContext.BaseDirectory, "cache", "plugin.log");
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
                TryCreateFormatter(type, name);
            }
        }
        catch (Exception ex)
        {
            Log($"加载插件 {name} 失败：{ex}");
        }
    }

    private void TryCreateFormatter(Type type, string assemblyName)
    {
        if (!typeof(ICodeFormatter).IsAssignableFrom(type)) return;
        if (type.IsAbstract || type.IsInterface || !(type.IsPublic || type.IsNestedPublic)) return;
        try
        {
            if (Activator.CreateInstance(type) is ICodeFormatter formatter)
            {
                lock (_gate)
                {
                    ICodeFormatter? existing = _formatters.FirstOrDefault(f =>
                        f.LanguageIds.Any(id => formatter.LanguageIds.Any(newId =>
                            string.Equals(id, newId, StringComparison.OrdinalIgnoreCase))));
                    if (existing is not null)
                    {
                        Log($"插件 {assemblyName} 的 {type.FullName} 与已加载的 {existing.GetType().FullName} 语言标识重复，已忽略");
                        return;
                    }
                    _formatters.Add(formatter);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"实例化插件类型 {type.FullName}（{assemblyName}）失败：{ex}");
        }
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
