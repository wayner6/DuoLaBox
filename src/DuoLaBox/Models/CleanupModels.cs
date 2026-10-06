namespace DuoLaBox.Models;

public enum CleanupSortMode
{
    Name,
    FileSize
}

public sealed class CleanupCandidate
{
    public required string Path { get; init; }

    public required string Rule { get; init; }

    public string Category { get; init; } = "自定义规则";

    public string Description { get; init; } = "由清理库中的自定义路径匹配。";

    public long Size { get; init; }

    public bool IsSelected { get; set; } = true;

    public string DisplaySize => CleanupSizeFormatter.Format(Size);

    public string DisplayExplanation => $"{Category} · {Description}";
}

public sealed record CleanupScanResult(
    IReadOnlyList<CleanupCandidate> Candidates,
    int TotalRuleCount,
    int RecognizedRuleCount,
    int InvalidRuleCount,
    int InaccessibleItemCount,
    bool ReachedLimit)
{
    public long TotalSize => Candidates.Sum(item => item.Size);
}

public sealed record CleanupExecutionResult(
    int DeletedCount,
    int FailedCount,
    long ReleasedBytes,
    IReadOnlyList<string>? DeletedPaths = null,
    IReadOnlyList<string>? FailedPaths = null,
    IReadOnlyList<CleanupFailure>? Failures = null);

public enum CleanupFailureKind
{
    InUse,
    AccessDenied,
    InvalidRule,
    ValidationRejected,
    Other
}

public sealed record CleanupFailure(
    string Path,
    CleanupFailureKind Kind);

public sealed record CleanupStagedItem(string Path, string Rule);

public static class CleanupSizeFormatter
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}

public static class CleanupCandidateSorter
{
    public static IReadOnlyList<CleanupCandidate> Sort(
        IEnumerable<CleanupCandidate> candidates,
        CleanupSortMode mode)
    {
        return mode == CleanupSortMode.FileSize
            ? candidates
                .OrderByDescending(item => item.Size)
                .ThenBy(
                    item => item.Path,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
            : candidates
                .OrderBy(
                    item => System.IO.Path.GetFileName(item.Path),
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(
                    item => item.Path,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
    }
}
