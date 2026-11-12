using System.IO;
using Microsoft.Win32;

namespace DuoLaBox.Services;

// 提权进程只接受位于 DuoLaBox 暂存目录中的任务文件。
// 标准用户提权时输入的是另一个管理员账户的凭据，提权进程的“当前用户”
// 因此不是发起任务的用户，不能只和自己的 LocalAppData 比较。
internal static class StagingLocation
{
    private const string ProfileListPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    internal static bool IsTrusted(string fullPath, string folderName) =>
        IsUnderAny(fullPath, GetRoots(folderName));

    internal static bool IsUnderAny(
        string fullPath,
        IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            var prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (fullPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<string> GetRoots(string folderName)
    {
        yield return AppIdentity.GetLocalDataPath(folderName);
        foreach (var profile in GetProfileDirectories())
        {
            yield return Path.Combine(
                profile,
                "AppData",
                "Local",
                AppIdentity.LocalDataFolderName,
                folderName);
        }
    }

    private static List<string> GetProfileDirectories()
    {
        var profiles = new List<string>();
        try
        {
            using var machine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64);
            using var list = machine.OpenSubKey(ProfileListPath);
            if (list is null)
            {
                return profiles;
            }

            foreach (var name in list.GetSubKeyNames())
            {
                using var profile = list.OpenSubKey(name);
                if (profile?.GetValue("ProfileImagePath") is string path &&
                    Path.IsPathFullyQualified(path))
                {
                    profiles.Add(path);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            // 读不到用户配置列表时，仍可使用当前用户自己的暂存目录。
        }

        return profiles;
    }
}
