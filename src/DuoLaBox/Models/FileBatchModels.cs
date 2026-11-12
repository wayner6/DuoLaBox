using System.IO;

namespace DuoLaBox.Models;

public sealed record RenamePreviewItem(
    string SourcePath,
    string DestinationPath)
{
    public string SourceName => Path.GetFileName(SourcePath);

    public string DestinationName => Path.GetFileName(DestinationPath);
}

// ApplicationType 是 Restart Manager 的 RM_APP_TYPE：3 服务，4 资源管理器，1000 系统关键进程。
public sealed record LockingProcessInfo(
    int ProcessId,
    string ApplicationName,
    string ServiceName,
    int ApplicationType = 0)
{
    public bool IsService => ApplicationType == 3;

    public bool IsExplorer => ApplicationType == 4;

    public bool IsCritical => ApplicationType == 1000;

    public string DisplayName =>
        IsCritical ? $"{ApplicationName}（系统关键进程，无法关闭）"
        : IsExplorer ? $"{ApplicationName}（Windows 资源管理器）"
        : IsService ? $"{ApplicationName}（系统服务）"
        : ApplicationName;
}

public enum FileToolMode
{
    Rename,
    Unlock,
    ResizeImages
}
