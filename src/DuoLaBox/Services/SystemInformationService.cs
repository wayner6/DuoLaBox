using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using DuoLaBox.Models;
using Microsoft.Win32;

namespace DuoLaBox.Services;

public sealed partial class SystemInformationService
{
    public Task<SystemInformationSnapshot> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Scan(cancellationToken),
            cancellationToken);
    }

    private static SystemInformationSnapshot Scan(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var wmi = new WmiReader();
        using var monitorWmi = new WmiReader(@"root\wmi");
        var computer = wmi.QueryFirst(
            "SELECT Manufacturer, Model, PCSystemType, TotalPhysicalMemory " +
            "FROM Win32_ComputerSystem");
        var operatingSystem = wmi.QueryFirst(
            "SELECT Caption, OSArchitecture FROM Win32_OperatingSystem");
        var processors = wmi.Query(
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors " +
            "FROM Win32_Processor");
        var baseBoards = wmi.Query(
            "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard");
        var memories = wmi.Query(
            "SELECT Capacity, ConfiguredClockSpeed, Speed, SMBIOSMemoryType " +
            "FROM Win32_PhysicalMemory");
        var graphicsCards = wmi.Query(
            "SELECT Name FROM Win32_VideoController");
        var monitors = wmi.Query(
            "SELECT Name, ScreenWidth, ScreenHeight FROM Win32_DesktopMonitor");
        var monitorIdentities = monitorWmi.Query(
            "SELECT UserFriendlyName, ManufacturerName " +
            "FROM WmiMonitorID WHERE Active = TRUE");
        var drives = wmi.Query(
            "SELECT Model, Size, MediaType FROM Win32_DiskDrive");
        var audioDevices = wmi.Query(
            "SELECT Name, Status FROM Win32_SoundDevice");
        cancellationToken.ThrowIfCancellationRequested();

        var manufacturer = NormalizeManufacturer(
            GetValue(computer, "Manufacturer") ??
            ReadRegistryString(
                Registry.LocalMachine,
                @"HARDWARE\DESCRIPTION\System\BIOS",
                "SystemManufacturer"));
        var model = GetValue(computer, "Model") ??
            ReadRegistryString(
                Registry.LocalMachine,
                @"HARDWARE\DESCRIPTION\System\BIOS",
                "SystemProductName");
        var modelSummary = JoinDistinct(manufacturer, model);
        if (string.IsNullOrWhiteSpace(modelSummary))
        {
            modelSummary = Environment.MachineName;
        }

        var computerType = FormatComputerType(
            GetUInt64(computer, "PCSystemType"));
        if (!string.IsNullOrWhiteSpace(computerType))
        {
            modelSummary = $"{modelSummary} · {computerType}";
        }

        var operatingSystemSummary = BuildOperatingSystemSummary(
            operatingSystem);
        var processorValues = BuildProcessorValues(processors);
        var boardValues = BuildBoardValues(baseBoards);
        var memoryValues = BuildMemoryValues(memories, computer);
        var graphicsValues = BuildGraphicsValues(graphicsCards);
        var displayValues = BuildDisplayValues(
            monitors,
            monitorIdentities);
        var driveValues = BuildDriveValues(drives);
        var audioValues = BuildAudioValues(audioDevices);
        var networkValues = BuildNetworkValues();

        return new SystemInformationSnapshot(
            modelSummary,
            operatingSystemSummary,
            DateTimeOffset.Now -
                TimeSpan.FromMilliseconds(Environment.TickCount64),
            [
                new SystemInformationGroup("处理器", processorValues),
                new SystemInformationGroup("主板", boardValues),
                new SystemInformationGroup("内存", memoryValues),
                new SystemInformationGroup("显卡", graphicsValues),
                new SystemInformationGroup("显示器", displayValues),
                new SystemInformationGroup("磁盘", driveValues),
                new SystemInformationGroup("声卡", audioValues),
                new SystemInformationGroup("网卡", networkValues)
            ]);
    }


}
