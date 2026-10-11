using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using HashCheck.Services;
using HashCheck.UI;

namespace HashCheck;

public enum AppMode
{
    Gui,
    SelfTest,
    Register,
    Unregister,
}

/// <summary>命令行参数。</summary>
public sealed record AppArgs(
    AppMode Mode,
    IReadOnlyList<string> Files,
    string? ReportPath,
    string? Error,
    bool ExportAfterCompute)
{
    public const string Usage =
        "Hash 校验工具 —— 用法：\r\n" +
        "\r\n" +
        "  HashCheck.exe                       启动界面\r\n" +
        "  HashCheck.exe <文件> [文件]          载入文件并计算 Hash\r\n" +
        "  HashCheck.exe --calc <文件> [文件]   同上\r\n" +
        "  HashCheck.exe --calc-export <文件>   计算并导出「文件名_Hash校验结果.txt」\r\n" +
        "  HashCheck.exe --register            添加资源管理器右键菜单（当前用户）\r\n" +
        "  HashCheck.exe --unregister          移除资源管理器右键菜单\r\n" +
        "  HashCheck.exe --selftest [报告文件]  运行内置自检（标准测试向量）";

    public static AppArgs Parse(string[] args)
    {
        var files = new List<string>();
        var mode = AppMode.Gui;
        var exportAfter = false;
        string? report = null;
        string? error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Length > 1 && (arg[0] == '-' || arg[0] == '/'))
            {
                switch (arg.TrimStart('-', '/').ToLowerInvariant())
                {
                    case "calc":
                        mode = AppMode.Gui;
                        break;
                    case "calc-export":
                        mode = AppMode.Gui;
                        exportAfter = true;
                        break;
                    case "register":
                        mode = AppMode.Register;
                        break;
                    case "unregister":
                        mode = AppMode.Unregister;
                        break;
                    case "selftest":
                        mode = AppMode.SelfTest;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            report = args[++i];
                        break;
                    case "h":
                    case "help":
                    case "?":
                        error = Usage;
                        break;
                    default:
                        error = "未知参数：" + arg + "\r\n\r\n" + Usage;
                        break;
                }
            }
            else
            {
                files.Add(arg);
            }
        }

        return new AppArgs(mode, files, report, error, exportAfter);
    }
}

internal static class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        // 命令行模式：GUI 程序没有控制台输出，出问题时全靠这个日志定位
        var cliMode = args.Length > 0 && (args[0].StartsWith("-") || args[0].StartsWith("/"));

        if (cliMode)
            AppendStartupLog("Main 进入，参数: " + string.Join(" ", args));

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            if (cliMode)
            {
                AppendStartupLog("未处理异常: " + ex);
                TryWriteError(ex.ToString());
            }
            else
            {
                MessageBox.Show(ex.ToString(), "发生未处理的错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return 3;
        }
    }

    private static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        var parsed = AppArgs.Parse(args);

        if (parsed.Error is not null)
        {
            AttachConsole(AttachParentProcess);
            Console.Error.WriteLine(parsed.Error);
            MessageBox.Show(parsed.Error, "Hash 校验工具", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 2;
        }

        switch (parsed.Mode)
        {
            case AppMode.SelfTest:
            {
                AttachConsole(AttachParentProcess);
                var exitCode = SelfTest.RunAsync(parsed.ReportPath).GetAwaiter().GetResult();
                AppendStartupLog("自检结束，退出码 " + exitCode);
                return exitCode;
            }

            case AppMode.Register:
                AttachConsole(AttachParentProcess);
                try
                {
                    ShellIntegration.Register();
                    Console.WriteLine("已添加资源管理器右键菜单（当前用户）。");
                    return 0;
                }
                catch (Exception ex)
                {
                    TryWriteError(ex.ToString());
                    return 1;
                }

            case AppMode.Unregister:
                AttachConsole(AttachParentProcess);
                try
                {
                    ShellIntegration.Unregister();
                    Console.WriteLine("已移除资源管理器右键菜单。");
                    return 0;
                }
                catch (Exception ex)
                {
                    TryWriteError(ex.ToString());
                    return 1;
                }
        }

        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.ToString(), "发生未处理的错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show(e.ExceptionObject?.ToString() ?? "未知错误", "发生未处理的错误", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.Run(new MainForm(parsed));
        return 0;
    }

    private static void AppendStartupLog(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "HashCheck.startup.log");
            File.AppendAllText(
                path,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                new UTF8Encoding(true));
        }
        catch
        {
            // 记日志失败不影响主流程
        }
    }

    private static void TryWriteError(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
            // 无控制台时忽略
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
}