using Microsoft.Win32;

namespace Mnemosyne.Services;

/// <summary>
/// Windows 资源管理器右键菜单注册，写 HKCU 无需管理员权限。
/// 文件：Software\Classes\*\shell\Mnemosyne；
/// 文件夹：Software\Classes\Directory\shell\Mnemosyne（右键文件夹本身）
/// 与 Software\Classes\Directory\Background\shell\Mnemosyne（文件夹内空白处右键，%V 即当前文件夹）。
/// </summary>
public static class ShellIntegrationService
{
    private const string FileKeyPath = @"Software\Classes\*\shell\Mnemosyne";
    private const string FolderKeyPath = @"Software\Classes\Directory\shell\Mnemosyne";
    private const string FolderBackgroundKeyPath = @"Software\Classes\Directory\Background\shell\Mnemosyne";

    /// <summary>按开关状态同步注册表：开则写入/覆盖，关则删除整棵子键</summary>
    public static void Apply(bool fileEnabled, bool folderEnabled, string fileMenuText, string folderMenuText)
    {
        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Process path is unavailable");
        if (fileEnabled)
        {
            Register(FileKeyPath, fileMenuText, exePath, "\"%1\"");
        }
        else
        {
            Unregister(FileKeyPath);
        }
        if (folderEnabled)
        {
            Register(FolderKeyPath, folderMenuText, exePath, "\"%V\"");
            Register(FolderBackgroundKeyPath, folderMenuText, exePath, "\"%V\"");
        }
        else
        {
            Unregister(FolderKeyPath);
            Unregister(FolderBackgroundKeyPath);
        }
    }

    private static void Register(string keyPath, string menuText, string exePath, string targetArg)
    {
        using RegistryKey shell = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        shell.SetValue(null, menuText);
        shell.SetValue("Icon", $"\"{exePath}\"");
        using RegistryKey command = shell.CreateSubKey("command", writable: true);
        command.SetValue(null, $"\"{exePath}\" {targetArg}");
    }

    private static void Unregister(string keyPath)
    {
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }
}
