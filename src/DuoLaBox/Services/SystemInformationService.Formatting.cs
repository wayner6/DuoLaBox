using System.Runtime.InteropServices;

namespace DuoLaBox.Services;

public sealed partial class SystemInformationService
{
    private static string FormatComputerType(ulong type) => type switch
    {
        1 => "台式电脑",
        2 => "笔记本电脑",
        3 => "工作站",
        4 => "服务器",
        8 => "平板电脑",
        _ => string.Empty
    };

    private static string FormatMemoryType(ulong type) => type switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => string.Empty
    };

    private static string FormatCapacity(ulong bytes)
    {
        var gibibytes = bytes / 1024d / 1024d / 1024d;
        return gibibytes >= 1024
            ? $"{gibibytes / 1024:0.##} TB"
            : $"{gibibytes:0.##} GB";
    }

    private static string JoinDistinct(params string?[] parts)
    {
        return string.Join(
            " ",
            parts
                .Where(part =>
                    !string.IsNullOrWhiteSpace(part) &&
                    !part.Equals(
                        "To be filled by O.E.M.",
                        StringComparison.OrdinalIgnoreCase) &&
                    !part.Equals(
                        "Default string",
                        StringComparison.OrdinalIgnoreCase))
                .Select(part => part!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string? NormalizeManufacturer(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        string[] suffixes =
        [
            " International Co., Ltd.",
            " International Co., Ltd",
            " Co., Ltd.",
            " Co., Ltd",
            " Corporation",
            " Inc."
        ];
        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(
                    suffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^suffix.Length].Trim();
                break;
            }
        }

        return normalized;
    }

    private static bool IsPhysicalGraphicsAdapter(string name)
    {
        string[] virtualMarkers =
        [
            "virtual",
            "remote display",
            "indirect display",
            "basic display"
        ];
        return virtualMarkers.All(marker =>
            !name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPhysicalNetworkAdapter(string name)
    {
        string[] virtualMarkers =
        [
            "virtual",
            "wi-fi direct",
            "hyper-v",
            "vpn",
            "tap-windows",
            "pseudo"
        ];
        return virtualMarkers.All(marker =>
            !name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> SingleOrUnavailable(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ["暂未读取到相关信息"]
            : [value.Trim()];
    }

    private static string? NormalizeDeviceName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(
                " ",
                value.Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // WMI can release nested objects with their owner.
        }
    }

    private static bool TryGetPhysicalMemory(out ulong bytes)
    {
        var status = new MemoryStatusEx();
        if (GlobalMemoryStatusEx(status))
        {
            bytes = status.TotalPhysical;
            return true;
        }

        bytes = 0;
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(
        [In, Out] MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
