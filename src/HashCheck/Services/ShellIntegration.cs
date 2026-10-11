using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HashCheck.Services;

/// <summary>
/// 资源管理器右键菜单集成。只写 HKCU，因此不需要管理员权限、也不影响其他用户。
/// Windows 11 会把这类静态菜单项收进“显示更多选项”二级菜单，这是系统自身的行为。
/// </summary>
public static class ShellIntegration
{
    private const string VerbsRoot = @"Software\Classes\*\shell";
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public const string CalcVerbName = "HashCheck.Calc";
    public const string ExportVerbName = "HashCheck.CalcExport";
    public const string CalcDisplayName = "计算Hash值";
    public const string ExportDisplayName = "计算Hash并导出";

    /// <summary>程序自身的 exe 路径，注册表里记录的就是它。</summary>
    public static string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path) ||
                string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                var exe = Application.ExecutablePath;
                path = exe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? Path.ChangeExtension(exe, ".exe")
                    : exe;
            }

            return path ?? string.Empty;
        }
    }

    public static bool IsRegistered() => IsRegistered(out _);

    public static bool IsRegistered(out string registeredExePath)
    {
        var calc = ReadRegisteredExePath(CalcVerbName);
        var export = ReadRegisteredExePath(ExportVerbName);
        registeredExePath = calc ?? string.Empty;

        return calc is not null && export is not null &&
               string.Equals(calc, export, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadRegisteredExePath(string verbName)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{VerbsRoot}\{verbName}\command");
        if (key?.GetValue(null) is not string command || string.IsNullOrWhiteSpace(command))
            return null;

        var start = command.IndexOf('"');
        if (start < 0)
            return null;

        var end = command.IndexOf('"', start + 1);
        return end < 0 ? null : command.Substring(start + 1, end - start - 1);
    }

    public static void Register(string? exePath = null)
    {
        var exe = string.IsNullOrWhiteSpace(exePath) ? ExecutablePath : exePath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new FileNotFoundException("找不到程序自身的 exe 路径，无法注册右键菜单。", exe);

        RegisterVerb(CalcVerbName, CalcDisplayName, exe, "--calc");
        RegisterVerb(ExportVerbName, ExportDisplayName, exe, "--calc-export");
        NotifyShellChanged();
    }

    private static void RegisterVerb(string verbName, string displayName, string exePath, string switchName)
    {
        using var verbKey = Registry.CurrentUser.CreateSubKey($@"{VerbsRoot}\{verbName}", true);
        if (verbKey is null)
            throw new InvalidOperationException(@"无法创建注册表项 HKCU\Software\Classes\*\shell。");

        verbKey.SetValue(null, displayName, RegistryValueKind.String);
        verbKey.SetValue("MUIVerb", displayName, RegistryValueKind.String);
        verbKey.SetValue("Icon", "\"" + exePath + "\",0", RegistryValueKind.String);
        verbKey.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);

        using var commandKey = verbKey.CreateSubKey("command", true);
        if (commandKey is null)
            throw new InvalidOperationException("无法创建注册表 command 子项。");

        commandKey.SetValue(null, "\"" + exePath + "\" " + switchName + " \"%1\"", RegistryValueKind.String);
    }

    public static void Unregister()
    {
        foreach (var verbName in new[] { CalcVerbName, ExportVerbName })
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{VerbsRoot}\{verbName}", false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("删除右键菜单项失败：" + verbName, ex);
            }
        }

        NotifyShellChanged();
    }

    private static void NotifyShellChanged()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // 通知资源管理器刷新失败不影响注册结果
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}