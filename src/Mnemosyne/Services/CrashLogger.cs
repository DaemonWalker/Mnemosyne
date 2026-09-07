using System.IO;

namespace Mnemosyne.Services;

public static class CrashLogger
{
    private const long MaxSizeBytes = 1024 * 1024;
    private static readonly object Sync = new();

    public static void Log(string source, Exception ex)
    {
        try
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "cache", "error.log");
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                RotateIfOversized(logPath);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // 日志失败不致命（便携目录可能只读）
        }
    }

    private static void RotateIfOversized(string logPath)
    {
        if (File.Exists(logPath) && new FileInfo(logPath).Length > MaxSizeBytes)
        {
            string oldPath = Path.Combine(Path.GetDirectoryName(logPath)!, "error.old.log");
            File.Move(logPath, oldPath, overwrite: true);
        }
    }
}
