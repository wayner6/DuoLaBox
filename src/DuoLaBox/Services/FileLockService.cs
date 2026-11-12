using System.IO;
using System.Text;
using DuoLaBox.Models;

namespace DuoLaBox.Services;

public sealed class FileLockService
{
    public Task<IReadOnlyList<LockingProcessInfo>> GetLockingProcessesAsync(
        IEnumerable<string> paths)
    {
        var files = paths.ToArray();
        return Task.Run(() => WithSession(files, GetProcessList));
    }

    // restoreShell：占用者包含资源管理器时，关闭后必须把任务栏和桌面恢复回来。
    public Task<bool> ReleaseLocksAsync(
        IEnumerable<string> paths,
        bool force = false,
        bool restoreShell = false)
    {
        var files = paths.ToArray();
        return Task.Run(() => WithSession(
            files,
            session =>
            {
                var released = NativeMethods.RmShutdown(
                    session,
                    force ? 0x1u : 0u,
                    nint.Zero) == 0;
                if (restoreShell)
                {
                    RestoreShell(session);
                }

                return released;
            }));
    }

    internal static LockReleasePlan PlanRelease(
        IReadOnlyList<LockingProcessInfo> processes,
        int currentProcessId)
    {
        if (processes.Any(process => process.ProcessId == currentProcessId))
        {
            return new LockReleasePlan(
                false,
                false,
                "DuoLaBox 自身正在使用这些文件，无法通过关闭程序来解除占用。请重启 DuoLaBox 后再试。");
        }

        if (processes.All(process => process.IsCritical))
        {
            return new LockReleasePlan(
                false,
                false,
                "文件由系统关键进程占用，无法解除。重启 Windows 后再处理这些文件。");
        }

        var names = string.Join(
            "、",
            processes.Select(process => process.DisplayName));
        var notes = new List<string>();
        var restoreShell = processes.Any(process => process.IsExplorer);
        if (restoreShell)
        {
            notes.Add("Windows 资源管理器会被关闭：任务栏和桌面会短暂消失后自动恢复，已打开的文件夹窗口会关闭。");
        }

        if (processes.Any(process => process.IsService))
        {
            notes.Add("系统服务被停止后不会自动重新启动，需要手动启动或重启电脑。");
        }

        if (processes.Any(process => process.IsCritical))
        {
            notes.Add("系统关键进程不会被关闭，文件可能仍被占用。");
        }

        var noteText = notes.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, notes) +
              Environment.NewLine + Environment.NewLine;
        return new LockReleasePlan(
            true,
            restoreShell,
            "将请求以下程序关闭以解除占用：" +
            Environment.NewLine + Environment.NewLine + names +
            Environment.NewLine + Environment.NewLine + noteText +
            "请先保存这些程序中的工作。是否继续？");
    }

    private static void RestoreShell(uint session)
    {
        // 提权运行时不能自己启动 explorer.exe，否则整个桌面都会以管理员身份运行；
        // 交给 Restart Manager 按原有权限恢复。
        if (Environment.IsPrivilegedProcess)
        {
            NativeMethods.RmRestart(session, 0, nint.Zero);
            return;
        }

        // 不调用 RmRestart：它会把刚关闭的其他程序一并重新打开，文件又会被占用。
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (NativeMethods.GetShellWindow() != nint.Zero)
            {
                return;
            }

            Thread.Sleep(150);
        }

        try
        {
            using var shell = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.Windows),
                        "explorer.exe"))
                {
                    UseShellExecute = false
                });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or
                InvalidOperationException or IOException)
        {
            // 用户仍可通过任务管理器运行 explorer.exe；这里不掩盖解除占用的结果。
        }
    }

    private static T WithSession<T>(
        IEnumerable<string> paths,
        Func<uint, T> action)
    {
        var files = paths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException("没有可检查的文件。");
        }

        var sessionKey = new StringBuilder(33);
        var result = NativeMethods.RmStartSession(
            out var session,
            0,
            sessionKey);
        if (result != 0)
        {
            if (result == 29)
            {
                throw new InvalidOperationException(
                    "Windows Restart Manager 当前不可用（错误 29）。" +
                    "请重启 Windows 后再尝试解除占用。");
            }

            throw new InvalidOperationException(
                $"无法启动 Windows Restart Manager（错误 {result}）。");
        }

        try
        {
            result = NativeMethods.RmRegisterResources(
                session,
                (uint)files.Length,
                files,
                0,
                [],
                0,
                []);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"无法检查文件占用（错误 {result}）。");
            }

            return action(session);
        }
        finally
        {
            NativeMethods.RmEndSession(session);
        }
    }

    private static IReadOnlyList<LockingProcessInfo> GetProcessList(
        uint session)
    {
        uint needed = 0;
        uint count = 0;
        uint rebootReasons = 0;
        var result = NativeMethods.RmGetList(
            session,
            out needed,
            ref count,
            null,
            ref rebootReasons);
        if (result == 0)
        {
            return [];
        }

        if (result != NativeMethods.ErrorMoreData)
        {
            throw new InvalidOperationException(
                $"无法读取占用进程（错误 {result}）。");
        }

        var processes =
            new NativeMethods.RestartManagerProcessInfo[needed];
        count = needed;
        result = NativeMethods.RmGetList(
            session,
            out needed,
            ref count,
            processes,
            ref rebootReasons);
        if (result != 0)
        {
            throw new InvalidOperationException(
                $"无法读取占用进程（错误 {result}）。");
        }

        return processes
            .Take((int)count)
            .Select(process => new LockingProcessInfo(
                process.Process.ProcessId,
                string.IsNullOrWhiteSpace(process.ApplicationName)
                    ? $"PID {process.Process.ProcessId}"
                    : process.ApplicationName,
                process.ServiceShortName ?? string.Empty,
                (int)process.ApplicationType))
            .DistinctBy(process => process.ProcessId)
            .ToArray();
    }
}

internal sealed record LockReleasePlan(
    bool CanProceed,
    bool RestoreShell,
    string Message);
