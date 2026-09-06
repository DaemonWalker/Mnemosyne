using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Mnemosyne.Models;

namespace Mnemosyne.Services;

/// <summary>
/// 热退出暂存（cache/hotexit/）与会话恢复（cache/session.json）。
/// 暂存布局：&lt;key&gt;.txt 为文档内容（UTF-8 无 BOM），&lt;key&gt;.json 为元数据。
/// key 对文件文档取全路径大写后的 SHA256 前 32 位十六进制（"f-" 前缀），新建文档用随机 GUID（"n-" 前缀）。
/// 所有方法容忍 IO 失败（缓存失败不致命），失败追加 cache/session.log。
/// </summary>
public class SessionService
{
    // UnsafeRelaxedJsonEscaping：暂存/会话文件里的中文标题不转义为 \uXXXX，便于排查
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _hotExitDir;
    private readonly string _sessionPath;
    private readonly string _logPath;

    public SessionService()
    {
        string cacheDir = Path.Combine(AppContext.BaseDirectory, "cache");
        _hotExitDir = Path.Combine(cacheDir, "hotexit");
        _sessionPath = Path.Combine(cacheDir, "session.json");
        _logPath = Path.Combine(cacheDir, "session.log");
        try
        {
            Directory.CreateDirectory(_hotExitDir);
        }
        catch (Exception)
        {
            // 目录创建失败时后续读写各自容错
        }
    }

    public static string KeyForPath(string path) =>
        "f-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..32];

    public static string NewKeyForUntitled() => "n-" + Guid.NewGuid().ToString("N");

    // ---- 热退出暂存 ----

    public void WriteStash(string key, HotExitStash metadata, string content)
    {
        try
        {
            File.WriteAllText(ContentPath(key), content, Utf8NoBom);
            File.WriteAllText(MetaPath(key), JsonSerializer.Serialize(metadata, JsonOptions), Utf8NoBom);
        }
        catch (Exception ex)
        {
            Log("WriteStash " + key, ex);
        }
    }

    /// <summary>读取暂存；不存在或损坏返回 null</summary>
    public (HotExitStash Metadata, string Content)? ReadStash(string key)
    {
        try
        {
            string contentPath = ContentPath(key);
            if (!File.Exists(contentPath)) return null;
            string content = File.ReadAllText(contentPath, Utf8NoBom);
            HotExitStash metadata = new();
            string metaPath = MetaPath(key);
            if (File.Exists(metaPath))
            {
                metadata = JsonSerializer.Deserialize<HotExitStash>(File.ReadAllText(metaPath, Utf8NoBom)) ?? new HotExitStash();
            }
            return (metadata, content);
        }
        catch (Exception ex)
        {
            Log("ReadStash " + key, ex);
            return null;
        }
    }

    public void ClearStash(string key)
    {
        try
        {
            string contentPath = ContentPath(key);
            string metaPath = MetaPath(key);
            if (File.Exists(contentPath)) File.Delete(contentPath);
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
        catch (Exception ex)
        {
            Log("ClearStash " + key, ex);
        }
    }

    /// <summary>磁盘上现存的全部暂存键（供启动时清理孤儿暂存）</summary>
    public IReadOnlyList<string> ListStashKeys()
    {
        try
        {
            return Directory.EnumerateFiles(_hotExitDir, "*.txt")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();
        }
        catch (Exception ex)
        {
            Log("ListStashKeys", ex);
            return [];
        }
    }

    // ---- 会话 ----

    public SessionState? LoadSession()
    {
        try
        {
            if (!File.Exists(_sessionPath)) return null;
            return JsonSerializer.Deserialize<SessionState>(File.ReadAllText(_sessionPath, Utf8NoBom));
        }
        catch (Exception ex)
        {
            Log("LoadSession", ex);
            return null;
        }
    }

    public void SaveSession(SessionState state)
    {
        try
        {
            File.WriteAllText(_sessionPath, JsonSerializer.Serialize(state, JsonOptions), Utf8NoBom);
        }
        catch (Exception ex)
        {
            Log("SaveSession", ex);
        }
    }

    private string ContentPath(string key) => Path.Combine(_hotExitDir, key + ".txt");

    private string MetaPath(string key) => Path.Combine(_hotExitDir, key + ".json");

    private void Log(string operation, Exception ex)
    {
        try
        {
            File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {operation}: {ex.Message}{Environment.NewLine}", Utf8NoBom);
        }
        catch (Exception)
        {
            // 日志失败不致命
        }
    }
}
