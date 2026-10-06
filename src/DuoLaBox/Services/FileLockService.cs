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

    public Task<bool> ReleaseLocksAsync(
        IEnumerable<string> paths,
        bool force = false)
    {
        var files = paths.ToArray();
        return Task.Run(() => WithSession(
            files,
            session => NativeMethods.RmShutdown(
                session,
                force ? 0x1u : 0u,
                nint.Zero) == 0));
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
                process.ServiceShortName ?? string.Empty))
            .DistinctBy(process => process.ProcessId)
            .ToArray();
    }
}
