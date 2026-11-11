using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DuoLaBox.Services;

internal static class CrashLog
{
    private const long MaximumLength = 1024 * 1024;
    private static readonly object Gate = new();

    internal static string LogPath =>
        AppIdentity.GetLocalDataPath("crash.log");

    // 记录失败时返回 false；日志本身不能再引发新的异常。
    internal static bool TryWrite(string source, Exception? exception) =>
        TryWrite(LogPath, source, exception);

    internal static bool TryWrite(
        string path,
        string source,
        Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) &&
                    new FileInfo(path).Length > MaximumLength)
                {
                    File.Delete(path);
                }

                var version = Assembly.GetExecutingAssembly()
                    .GetName().Version?.ToString(3);
                File.AppendAllText(
                    path,
                    $"[{DateTimeOffset.Now:O}] {source} · " +
                    $"{AppIdentity.Name} {version} · " +
                    $"{Environment.OSVersion.VersionString} · " +
                    $"{RuntimeInformation.ProcessArchitecture}" +
                    Environment.NewLine +
                    (exception?.ToString() ?? "未知错误") +
                    Environment.NewLine + Environment.NewLine);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
