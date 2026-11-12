using System.Windows;
using DuoLaBox.Services;

namespace DuoLaBox;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private bool _startupCompleted;
    private bool _crashDialogOpen;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException +=
            CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException +=
            TaskScheduler_UnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startupArguments = e.Args.Length > 0
            ? e.Args
            : Environment.GetCommandLineArgs().Skip(1).ToArray();

        var hostsArgumentIndex = Array.FindIndex(
            startupArguments,
            argument => string.Equals(
                argument,
                "--apply-hosts",
                StringComparison.OrdinalIgnoreCase));
        if (hostsArgumentIndex >= 0 &&
            hostsArgumentIndex + 2 < startupArguments.Length)
        {
            var result = new HostsFileService().ApplyStagedContent(
                startupArguments[hostsArgumentIndex + 1],
                startupArguments[hostsArgumentIndex + 2]);
            Shutdown(result.Success ? 0 : 1);
            return;
        }

        var cleanupArgumentIndex = Array.FindIndex(
            startupArguments,
            argument => string.Equals(
                argument,
                "--apply-cleanup",
                StringComparison.OrdinalIgnoreCase));
        if (cleanupArgumentIndex >= 0 &&
            cleanupArgumentIndex + 2 < startupArguments.Length)
        {
            try
            {
                var result = new DiskCleanupService().ExecuteStaged(
                    startupArguments[cleanupArgumentIndex + 1],
                    startupArguments[cleanupArgumentIndex + 2]);
                Shutdown(result.FailedCount == 0 ? 0 : 2);
            }
            catch (Exception exception)
            {
                // 主程序在没有结果文件时会提示用户；提权进程不再弹窗。
                CrashLog.TryWrite("管理员清理", exception);
                Shutdown(3);
            }
            return;
        }

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            // 资源管理器多选时会同时启动很多个进程，它们要排队等主实例逐个接收。
            var forwarded = await _singleInstance.ForwardArgumentsAsync(
                startupArguments,
                timeoutMilliseconds: 15000);
            if (!forwarded)
            {
                System.Windows.MessageBox.Show(
                    "未能确认参数已转交给正在运行的 DuoLaBox。请返回已有窗口后重试。",
                    AppIdentity.Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            Shutdown(forwarded ? 0 : 1);
            return;
        }

        // 先开始监听，再创建主窗口：后续实例不必等主窗口建好才能连上。
        // 收到的参数排在界面线程队列里，本方法返回（窗口已就绪）之后才会执行。
        MainWindow? window = null;
        _singleInstance.ArgumentsReceived += arguments =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(() => window?.HandleStartupArguments(arguments));
            }
        };
        _singleInstance.StartListening();

        // 只有主实例执行：此时没有别的实例正在取色。
        EyedropperCursorService.RecoverAfterAbnormalExit();
        window = new MainWindow();
        MainWindow = window;
        window.InitializeStartup();
        _startupCompleted = true;

        var silentArgument = startupArguments.Any(argument =>
                string.Equals(
                    argument,
                    "--silent",
                    StringComparison.OrdinalIgnoreCase));
        var forceShow = startupArguments.Any(argument =>
                string.Equals(
                    argument,
                    "--show",
                    StringComparison.OrdinalIgnoreCase));
        if (forceShow)
        {
            window.Show();
            window.Activate();
            return;
        }

        // 静默只来自开机启动项写入的 --silent；用户手动打开时始终显示窗口。
        var silentStartup =
            silentArgument &&
            !startupArguments.Any(argument =>
                string.Equals(
                    argument,
                    "--file-tool",
                    StringComparison.OrdinalIgnoreCase));
        if (silentStartup)
        {
            return;
        }

        window.Show();
        window.Activate();
        if (startupArguments.Length > 0)
        {
            window.HandleStartupArguments(startupArguments);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        RestoreSystemCursors();
        var logged = CrashLog.TryWrite("界面线程", e.Exception);
        if (!_startupCompleted)
        {
            // 主窗口尚未就绪，继续运行只会留下没有界面的进程。
            ShowCrashMessage(
                "DuoLaBox 启动失败。",
                e.Exception,
                logged);
            Shutdown(1);
            return;
        }

        ShowCrashMessage(
            "DuoLaBox 遇到未处理的错误，当前操作可能没有完成。",
            e.Exception,
            logged);
    }

    private static void CurrentDomain_UnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        // 进程即将终止：先把选窗/取色替换的系统指针恢复。
        RestoreSystemCursors();
        CrashLog.TryWrite("后台线程", e.ExceptionObject as Exception);
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.TryWrite("后台任务", e.Exception);
        e.SetObserved();
    }

    private void ShowCrashMessage(
        string summary,
        Exception exception,
        bool logged)
    {
        // 定时器等重复触发的错误只提示一次，其余仅记录日志。
        if (_crashDialogOpen)
        {
            return;
        }

        _crashDialogOpen = true;
        try
        {
            System.Windows.MessageBox.Show(
                summary + Environment.NewLine + Environment.NewLine +
                exception.Message + Environment.NewLine + Environment.NewLine +
                (logged
                    ? $"详细信息已写入：{CrashLog.LogPath}"
                    : "详细信息未能写入日志。"),
                AppIdentity.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _crashDialogOpen = false;
        }
    }

    private static void RestoreSystemCursors()
    {
        NativeMethods.SystemParametersInfo(
            NativeMethods.SpiSetCursors,
            0,
            nint.Zero,
            0);
    }

}
