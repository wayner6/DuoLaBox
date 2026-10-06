using System.IO;

namespace DuoLaBox;

public static class AppIdentity
{
    public const string Name = "DuoLaBox";
    public const string Tagline = "轻量 · 开放 · 实用";
    public const string LocalDataFolderName = Name;
    public const string LegacyLocalDataFolderName = "WindowsToolbox";

    public static string LocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        LocalDataFolderName);

    public static string LegacyLocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        LegacyLocalDataFolderName);

    public static string GetLocalDataPath(params string[] segments) =>
        Path.Combine([LocalDataDirectory, .. segments]);

    public static string GetLegacyLocalDataPath(params string[] segments) =>
        Path.Combine([LegacyLocalDataDirectory, .. segments]);
}
