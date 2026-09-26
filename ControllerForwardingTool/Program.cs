using Avalonia;
using System;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = SingleInstanceService.StartOrActivate(App.RequestMainWindowActivation);
        if (instance is null) return;

        using var log = ApplicationLog.Current;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write("未处理异常", e.ExceptionObject.ToString() ?? "未知异常");
        TaskScheduler.UnobservedTaskException += (_, e) => log.Write("后台异常", e.Exception.ToString());
        try
        {
            // Relative writes by dependencies also belong to the current user's data.
            Environment.CurrentDirectory = AppDataPaths.EnsureRuntimeDirectory();
            log.Write("启动", $"版本={typeof(Program).Assembly.GetName().Version}；PID={Environment.ProcessId}；程序目录={AppContext.BaseDirectory}；数据目录={AppDataPaths.DirectoryPath}");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) { log.Write("致命错误", ex.ToString()); throw; }
        finally { log.Write("退出", "应用进程结束"); }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
