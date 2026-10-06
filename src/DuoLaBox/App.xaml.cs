using System.Windows;
using DuoLaBox.Services;

namespace DuoLaBox;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;

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
            var result = new DiskCleanupService().ExecuteStaged(
                startupArguments[cleanupArgumentIndex + 1],
                startupArguments[cleanupArgumentIndex + 2]);
            Shutdown(result.FailedCount == 0 ? 0 : 2);
            return;
        }

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            var forwarded = await _singleInstance.ForwardArgumentsAsync(startupArguments);
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

        var window = new MainWindow();
        MainWindow = window;
        window.InitializeStartup();
        _singleInstance.ArgumentsReceived += arguments =>
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(() => window.HandleStartupArguments(arguments));
            }
        };
        _singleInstance.StartListening();

        var silentArgument = startupArguments.Any(argument =>
                string.Equals(
                    argument,
                    "--silent",
                    StringComparison.OrdinalIgnoreCase));
        var forceShow = startupArguments.Any(argument =>
                string.Equals(
                    argument,
                    "--show",
                    StringComparison.OrdinalIgnoreCase)) ||
            Environment.CommandLine.Contains(
                "--show",
                StringComparison.OrdinalIgnoreCase);
        if (forceShow)
        {
            window.Show();
            window.Activate();
            return;
        }

        var silentStartup =
            (silentArgument ||
             (startupArguments.Length == 0 &&
              window.SilentStartupEnabled)) &&
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

}
