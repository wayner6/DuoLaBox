namespace DuoLaBox.Models;

public sealed class WindowInfo
{
    public System.Windows.Media.ImageSource? Icon { get; init; }

    public required nint Handle { get; init; }

    public required string Title { get; init; }

    public required string ProcessName { get; init; }

    public required uint ProcessId { get; init; }

    public required bool IsTopMost { get; init; }

    public string DisplayProcessName =>
        string.IsNullOrWhiteSpace(ProcessName) ? $"PID {ProcessId}" : ProcessName;

    public string ActionText => IsTopMost ? "取消置顶" : "置顶";

    public string StatusText => IsTopMost ? "已置顶" : "普通";
}
