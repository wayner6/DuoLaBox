using System.IO;

namespace DuoLaBox.Models;

public sealed record RenamePreviewItem(
    string SourcePath,
    string DestinationPath)
{
    public string SourceName => Path.GetFileName(SourcePath);

    public string DestinationName => Path.GetFileName(DestinationPath);
}

public sealed record LockingProcessInfo(
    int ProcessId,
    string ApplicationName,
    string ServiceName);

public enum FileToolMode
{
    Rename,
    Unlock,
    ResizeImages
}
