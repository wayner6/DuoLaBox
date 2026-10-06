using System.IO;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace DuoLaBox.Services;

public sealed partial class SystemInformationService
{
    private static string BuildOperatingSystemSummary(
        IReadOnlyDictionary<string, object?>? operatingSystem)
    {
        var productName = GetValue(operatingSystem, "Caption") ??
            ReadRegistryString(
                Registry.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "ProductName") ??
            "Windows";
        productName = productName
            .Replace("Microsoft ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        var displayVersion = ReadRegistryString(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "DisplayVersion");
        var architecture = GetValue(operatingSystem, "OSArchitecture") ??
            (Environment.Is64BitOperatingSystem ? "64 位" : "32 位");
        architecture = architecture
            .Replace("-bit", " 位", StringComparison.OrdinalIgnoreCase)
            .Replace("bit", " 位", StringComparison.OrdinalIgnoreCase);
        return JoinDistinct(productName, displayVersion, architecture);
    }

    private static IReadOnlyList<string> BuildProcessorValues(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> processors)
    {
        var values = processors
            .Select(item =>
            {
                var name = NormalizeDeviceName(GetValue(item, "Name"));
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                var cores = GetUInt64(item, "NumberOfCores");
                var threads = GetUInt64(item, "NumberOfLogicalProcessors");
                var topology = cores > 0 && threads > 0
                    ? $" · {cores} 核 / {threads} 线程"
                    : string.Empty;
                return name + topology;
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length > 0)
        {
            return values;
        }

        var registryName = ReadRegistryString(
            Registry.LocalMachine,
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString");
        return SingleOrUnavailable(registryName);
    }

    private static IReadOnlyList<string> BuildBoardValues(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> boards)
    {
        var values = boards
            .Select(item => JoinDistinct(
                NormalizeManufacturer(
                    GetValue(item, "Manufacturer")),
                GetValue(item, "Product"),
                GetValue(item, "Version")))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length > 0)
        {
            return values;
        }

        return SingleOrUnavailable(JoinDistinct(
            NormalizeManufacturer(ReadRegistryString(
                Registry.LocalMachine,
                @"HARDWARE\DESCRIPTION\System\BIOS",
                "BaseBoardManufacturer")),
            ReadRegistryString(
                Registry.LocalMachine,
                @"HARDWARE\DESCRIPTION\System\BIOS",
                "BaseBoardProduct")));
    }

    private static IReadOnlyList<string> BuildMemoryValues(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> memories,
        IReadOnlyDictionary<string, object?>? computer)
    {
        var totalBytes = memories.Sum(item =>
            (decimal)GetUInt64(item, "Capacity"));
        if (totalBytes <= 0)
        {
            totalBytes = GetUInt64(computer, "TotalPhysicalMemory");
        }

        if (totalBytes <= 0 && TryGetPhysicalMemory(out var nativeBytes))
        {
            totalBytes = nativeBytes;
        }

        var memoryType = memories
            .Select(item => FormatMemoryType(
                GetUInt64(item, "SMBIOSMemoryType")))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var speed = memories
            .Select(item => Math.Max(
                GetUInt64(item, "ConfiguredClockSpeed"),
                GetUInt64(item, "Speed")))
            .DefaultIfEmpty(0UL)
            .Max();
        var parts = new List<string>();
        if (totalBytes > 0)
        {
            parts.Add(FormatCapacity((ulong)totalBytes));
        }

        if (!string.IsNullOrWhiteSpace(memoryType))
        {
            parts.Add(memoryType);
        }

        if (speed > 0)
        {
            parts.Add($"{speed} MHz");
        }

        if (memories.Count > 0)
        {
            parts.Add($"{memories.Count} 条");
        }

        return SingleOrUnavailable(string.Join(" · ", parts));
    }

    private static IReadOnlyList<string> BuildGraphicsValues(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> cards)
    {
        var memoryMap = ReadGraphicsMemory();
        var values = cards
            .Select(item => NormalizeDeviceName(GetValue(item, "Name")))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Where(IsPhysicalGraphicsAdapter)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                var match = memoryMap.FirstOrDefault(pair =>
                    name.Contains(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                    pair.Key.Contains(name, StringComparison.OrdinalIgnoreCase));
                return match.Value > 0
                    ? $"{name} · {FormatCapacity(match.Value)}"
                    : name;
            })
            .ToArray();
        if (values.Length > 0)
        {
            return values;
        }

        return ReadGraphicsNamesFromRegistry();
    }

    private static IReadOnlyList<string> BuildDisplayValues(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> monitors,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> identities)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var values = new List<string>();
        for (var index = 0; index < screens.Length; index++)
        {
            var screen = screens[index];
            var friendlyName = index < identities.Count
                ? GetCharacterArrayValue(
                    identities[index],
                    "UserFriendlyName")
                : null;
            var manufacturer = index < identities.Count
                ? GetCharacterArrayValue(
                    identities[index],
                    "ManufacturerName")
                : null;
            var monitorName = !string.IsNullOrWhiteSpace(friendlyName)
                ? JoinDistinct(manufacturer, friendlyName)
                : index < monitors.Count
                ? NormalizeDeviceName(GetValue(monitors[index], "Name"))
                : null;
            if (string.IsNullOrWhiteSpace(monitorName) ||
                monitorName.Equals(
                    "Default Monitor",
                    StringComparison.OrdinalIgnoreCase))
            {
                monitorName = $"显示器 {index + 1}";
            }

            var primary = screen.Primary ? " · 主显示器" : string.Empty;
            values.Add(
                $"{monitorName} · {screen.Bounds.Width} × " +
                $"{screen.Bounds.Height}{primary}");
        }

        if (values.Count > 0)
        {
            return values;
        }

        return monitors
            .Select(item => NormalizeDeviceName(GetValue(item, "Name")))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Where(value =>
                !value.Contains(
                    "virtual",
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty("暂未检测到显示器")
            .ToArray();
    }

    private static IReadOnlyList<string> BuildDriveValues(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> drives)
    {
        var values = drives
            .Select(item =>
            {
                var model = NormalizeDeviceName(GetValue(item, "Model"));
                var size = GetUInt64(item, "Size");
                if (string.IsNullOrWhiteSpace(model))
                {
                    return null;
                }

                return size > 0
                    ? $"{model} · {FormatCapacity(size)}"
                    : model;
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length > 0)
        {
            return values;
        }

        try
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive =>
                    $"{drive.Name.TrimEnd('\\')} {drive.VolumeLabel}".Trim() +
                    $" · {FormatCapacity((ulong)drive.TotalSize)}")
                .ToArray();
        }
        catch
        {
            return ["暂未读取到磁盘信息"];
        }
    }

    private static IReadOnlyList<string> BuildAudioValues(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> devices)
    {
        var values = devices
            .Where(item =>
                !string.Equals(
                    GetValue(item, "Status"),
                    "Error",
                    StringComparison.OrdinalIgnoreCase))
            .Select(item => NormalizeDeviceName(GetValue(item, "Name")))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length > 0
            ? values
            : ["暂未读取到声卡信息"];
    }

    private static IReadOnlyList<string> BuildNetworkValues()
    {
        try
        {
            var values = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item =>
                    item.NetworkInterfaceType is not
                        NetworkInterfaceType.Loopback and not
                        NetworkInterfaceType.Tunnel &&
                    item.GetPhysicalAddress().GetAddressBytes().Length > 0)
                .OrderByDescending(item =>
                    item.OperationalStatus == OperationalStatus.Up)
                .Select(item => NormalizeDeviceName(
                    string.IsNullOrWhiteSpace(item.Description)
                        ? item.Name
                        : item.Description))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Where(IsPhysicalNetworkAdapter)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return values.Length > 0
                ? values
                : ["暂未检测到物理网卡"];
        }
        catch
        {
            return ["暂未读取到网卡信息"];
        }
    }


}
