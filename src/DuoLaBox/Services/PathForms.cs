using System.Text;

namespace DuoLaBox.Services;

// %TEMP% 等环境变量常是 8.3 短路径（C:\Users\LONGUS~1\...），比较路径前需要统一形式。
internal static class PathForms
{
    internal static string? TryGetLongPath(string path) =>
        Convert(path, NativeMethods.GetLongPathName);

    internal static string? TryGetShortPath(string path) =>
        Convert(path, NativeMethods.GetShortPathName);

    // 超过 MAX_PATH 的路径直接传给 Win32 时需要 \\?\ 前缀。
    internal static string ToDevicePath(string fullPath)
    {
        if (fullPath.Length < 248 ||
            fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath[2..]
            : @"\\?\" + fullPath;
    }

    internal static string FromDevicePath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? path[4..]
            : path;
    }

    private static string? Convert(
        string path,
        Func<string, StringBuilder, uint, uint> convert)
    {
        var source = ToDevicePath(path);
        var buffer = new StringBuilder(Math.Max(260, source.Length + 64));
        var length = convert(source, buffer, (uint)buffer.Capacity);
        if (length > buffer.Capacity)
        {
            buffer = new StringBuilder((int)length);
            length = convert(source, buffer, (uint)buffer.Capacity);
        }

        return length == 0 || length > buffer.Capacity
            ? null
            : FromDevicePath(buffer.ToString());
    }
}
