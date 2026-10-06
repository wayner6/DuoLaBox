namespace DuoLaBox.Models;

public sealed record SystemInformationGroup(
    string Label,
    IReadOnlyList<string> Values);

public sealed record SystemInformationSnapshot(
    string ModelSummary,
    string OperatingSystemSummary,
    DateTimeOffset BootTime,
    IReadOnlyList<SystemInformationGroup> Groups);
