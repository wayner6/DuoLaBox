using System.IO;

namespace DuoLaBox.Services;

// 与清理库内容无关的保护：自定义规则同样受这些限制。
internal static class CleanupSafety
{
    internal static readonly TimeSpan MinimumTemporaryAge =
        TimeSpan.FromHours(24);

    private static readonly Lazy<string[]> TemporaryRoots = new(() =>
    {
        var windows = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        return BuildRoots(
            Environment.GetEnvironmentVariable("TEMP"),
            Environment.GetEnvironmentVariable("TMP"),
            Path.GetTempPath(),
            Path.Combine(windows, "Temp"),
            Path.Combine(windows, "SystemTemp"),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "TEMP"));
    });

    // 单文件 .NET 程序（包括 DuoLaBox 自身）把运行所需的原生库解压到这里。
    private static readonly Lazy<string[]> RuntimeExtractionRoots = new(() =>
        BuildRoots(
            Environment.GetEnvironmentVariable(
                "DOTNET_BUNDLE_EXTRACT_BASE_DIR"),
            CombineOrNull(Environment.GetEnvironmentVariable("TEMP"), ".net"),
            CombineOrNull(Environment.GetEnvironmentVariable("TMP"), ".net"),
            Path.Combine(Path.GetTempPath(), ".net")));

    internal static bool IsRuntimeExtractionPath(string fullPath) =>
        IsUnder(fullPath, RuntimeExtractionRoots.Value);

    // 临时目录里刚写入的文件可能仍被安装程序或其他软件使用，即使没有加锁。
    internal static bool IsRecentTemporaryFile(
        string fullPath,
        FileInfo info,
        DateTime utcNow) =>
        IsRecentTemporaryFile(
            fullPath,
            info.LastWriteTimeUtc,
            info.CreationTimeUtc,
            utcNow,
            TemporaryRoots.Value);

    internal static bool IsRecentTemporaryFile(
        string fullPath,
        DateTime lastWriteUtc,
        DateTime creationUtc,
        DateTime utcNow,
        IReadOnlyList<string> temporaryRoots)
    {
        if (!IsUnder(fullPath, temporaryRoots))
        {
            return false;
        }

        var newest = lastWriteUtc > creationUtc ? lastWriteUtc : creationUtc;
        return utcNow - newest < MinimumTemporaryAge;
    }

    internal static bool IsUnder(
        string fullPath,
        IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // 每个目录同时登记原样、长路径和 8.3 短路径三种写法，末尾带分隔符。
    internal static string[] BuildRoots(params string?[] directories)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(directory);
                foreach (var form in new[]
                         {
                             full,
                             PathForms.TryGetLongPath(full),
                             PathForms.TryGetShortPath(full)
                         })
                {
                    if (!string.IsNullOrEmpty(form))
                    {
                        roots.Add(form.TrimEnd('\\') + "\\");
                    }
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or
                    IOException or System.Security.SecurityException)
            {
                // 无法解析的环境变量值不构成保护范围。
            }
        }

        return [.. roots];
    }

    private static string? CombineOrNull(string? directory, string child) =>
        string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.Combine(directory, child);
}
