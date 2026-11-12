using System.Diagnostics;
using System.IO;
using DuoLaBox;
using DuoLaBox.Models;
using DuoLaBox.Services;

// Covers the protections around deleting files and elevated helpers. Works only inside a new temp directory.
internal static class SafetyChecks
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DuoLaBoxSafety-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            VerifyTemporaryFileProtection(directory);
            VerifyVerifiedDelete(directory);
            VerifyVerifiedExecution(directory);
            VerifyStagingLocation();
            VerifyExpandedRules();
            VerifyReleasePlan();
            VerifyWindowPickerLifecycle();
        }
        finally
        {
            // A failed check can leave a read-only fixture behind; cleanup must not hide that failure.
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifyTemporaryFileProtection(string directory)
    {
        var longDirectory = Path.Combine(directory, "Long Temporary Directory Name");
        Directory.CreateDirectory(longDirectory);
        var roots = CleanupSafety.BuildRoots(longDirectory, null, " ");
        var file = Path.Combine(longDirectory, "a.tmp");
        var now = DateTime.UtcNow;
        var old = now.AddDays(-30);
        var recent = now.AddHours(-1);
        Assert(CleanupSafety.IsRecentTemporaryFile(file, recent, old, now, roots),
            "Recently written temporary file was not protected.");
        Assert(CleanupSafety.IsRecentTemporaryFile(file, old, recent, now, roots),
            "Recently copied temporary file was not protected.");
        Assert(!CleanupSafety.IsRecentTemporaryFile(file, old, old, now, roots),
            "Old temporary file was protected.");
        Assert(!CleanupSafety.IsRecentTemporaryFile(longDirectory + "Sibling\\a.tmp", recent, recent, now, roots),
            "A sibling directory was treated as the temporary directory.");

        // TEMP is often an 8.3 path; a rule written either way must hit the same protection.
        var shortDirectory = PathForms.TryGetShortPath(longDirectory);
        if (shortDirectory is not null &&
            !shortDirectory.Equals(longDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Assert(CleanupSafety.IsRecentTemporaryFile(
                    Path.Combine(shortDirectory, "a.tmp"), recent, recent, now, roots) &&
                CleanupSafety.IsUnder(file, CleanupSafety.BuildRoots(shortDirectory)),
                "Short and long forms of the temporary directory were not treated alike.");
        }

        Assert(CleanupSafety.IsRuntimeExtractionPath(
                Path.Combine(Path.GetTempPath(), ".net", "DuoLaBox", "hash", "wpfgfx_cor3.dll")) &&
            !CleanupSafety.IsRuntimeExtractionPath(Path.Combine(Path.GetTempPath(), ".network.tmp")),
            "Runtime extraction directory was not recognized exactly.");
    }

    private static void VerifyVerifiedDelete(string directory)
    {
        var plain = Path.Combine(directory, "plain file.tmp");
        File.WriteAllText(plain, "safe test data");
        VerifiedFileDelete.Delete(plain);
        Assert(!File.Exists(plain), "Verified delete left the file in place.");

        var readOnly = Path.Combine(directory, "readonly.tmp");
        File.WriteAllText(readOnly, "safe test data");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        VerifiedFileDelete.Delete(readOnly);
        Assert(!File.Exists(readOnly), "Verified delete did not remove a read-only file.");

        var shortSource = Path.Combine(directory, "long file name for short path.tmp");
        File.WriteAllText(shortSource, "safe test data");
        var shortPath = PathForms.TryGetShortPath(shortSource);
        if (shortPath is not null && !shortPath.Equals(shortSource, StringComparison.OrdinalIgnoreCase))
        {
            VerifiedFileDelete.Delete(shortPath);
            Assert(!File.Exists(shortSource), "Verified delete rejected an 8.3 path to the same file.");
        }

        try
        {
            VerifiedFileDelete.Delete(Path.Combine(directory, "missing.tmp"));
            throw new InvalidOperationException("Verified delete accepted a missing file.");
        }
        catch (FileNotFoundException)
        {
        }

        var real = Path.Combine(directory, "real");
        Directory.CreateDirectory(real);
        var victim = Path.Combine(real, "victim.txt");
        File.WriteAllText(victim, "must survive");
        var link = Path.Combine(directory, "link");
        CreateJunction(link, real);
        try
        {
            try
            {
                VerifiedFileDelete.Delete(Path.Combine(link, "victim.txt"));
                throw new InvalidOperationException("Verified delete followed a junction.");
            }
            catch (CleanupPathChangedException)
            {
            }
            Assert(File.Exists(victim), "File behind a junction was deleted.");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    private static void VerifyVerifiedExecution(string directory)
    {
        var root = Path.Combine(directory, "execute");
        Directory.CreateDirectory(root);
        var rule = root + Path.DirectorySeparatorChar;
        var ordinary = Path.Combine(root, "ordinary.tmp");
        File.WriteAllText(ordinary, "safe test data");
        var outside = Path.Combine(directory, "outside");
        Directory.CreateDirectory(outside);
        var victim = Path.Combine(outside, "victim.txt");
        File.WriteAllText(victim, "must survive");
        var link = Path.Combine(root, "swapped");
        CreateJunction(link, outside);
        try
        {
            var throughLink = Path.Combine(link, "victim.txt");
            var extraction = Path.Combine(Path.GetTempPath(), ".net", "DuoLaBox", "hash", "native.dll");
            var result = DiskCleanupService.Execute(
                [
                    new CleanupStagedItem(ordinary, rule),
                    new CleanupStagedItem(throughLink, rule),
                    new CleanupStagedItem(extraction, Path.GetTempPath()),
                    new CleanupStagedItem(ordinary, "%TEMP%\\")
                ],
                verifyOpenedPath: true,
                rulesExpanded: true);
            var failures = result.Failures ?? [];
            Assert(result.DeletedCount == 1 && !File.Exists(ordinary), "Verified execution did not delete the ordinary file.");
            Assert(File.Exists(victim) &&
                failures.Any(item => item.Path == throughLink && item.Kind == CleanupFailureKind.ValidationRejected),
                "Verified execution deleted or misreported a file reached through a junction.");
            Assert(failures.Any(item => item.Path == extraction && item.Kind == CleanupFailureKind.ValidationRejected),
                "Runtime extraction file was not rejected.");
            Assert(failures.Any(item => item.Kind == CleanupFailureKind.InvalidRule),
                "An unexpanded rule was expanded again by the elevated helper.");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    private static void VerifyStagingLocation()
    {
        const string root = @"C:\Users\someone\AppData\Local\DuoLaBox\HostsStaging";
        Assert(StagingLocation.IsUnderAny(root + @"\hosts-1.txt", [root]) &&
            StagingLocation.IsUnderAny(root.ToUpperInvariant() + @"\hosts-1.txt", [root + "\\"]),
            "Staged file inside the staging directory was rejected.");
        Assert(!StagingLocation.IsUnderAny(root + @"Evil\hosts-1.txt", [root]) &&
            !StagingLocation.IsUnderAny(Path.GetFullPath(root + @"\..\hosts-1.txt"), [root]) &&
            !StagingLocation.IsUnderAny(root, [root]),
            "A path outside the staging directory was trusted.");

        Assert(StagingLocation.IsTrusted(AppIdentity.GetLocalDataPath("HostsStaging", "hosts-1.txt"), "HostsStaging") &&
            !StagingLocation.IsTrusted(AppIdentity.GetLocalDataPath("HostsStaging", "hosts-1.txt"), "CleanupStaging") &&
            !StagingLocation.IsTrusted(Path.Combine(Path.GetTempPath(), "hosts-1.txt"), "HostsStaging"),
            "Current user's staging directory was not matched exactly.");

        // An elevated helper started with another administrator's credentials finds the
        // requesting user's directory through the profile list, not its own LocalAppData.
        var viaProfileList = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "Local", AppIdentity.LocalDataFolderName, "CleanupStaging");
        Assert(StagingLocation.GetRoots("CleanupStaging").Skip(1)
                .Contains(viaProfileList, StringComparer.OrdinalIgnoreCase),
            "Profile list did not yield this user's staging directory.");
    }

    private static void VerifyExpandedRules()
    {
        const string rule = "%TEMP%\\duolabox-check\\*.tmp";
        Assert(!CleanupRuleParser.TryPrepare(rule, out _, expand: false),
            "An unexpanded rule was accepted without expansion.");
        var expanded = CleanupRuleParser.Expand(rule);
        Assert(CleanupRuleParser.TryPrepare(rule, out var normal) &&
            CleanupRuleParser.TryPrepare(expanded, out var staged, expand: false) &&
            normal.Directory == staged.Directory &&
            CleanupRuleParser.IsFileAllowed(Path.Combine(staged.Directory, "a.tmp"), staged),
            "A staged rule did not match the same files as the original rule.");
    }

    private static void VerifyReleasePlan()
    {
        var word = new LockingProcessInfo(10, "Word", string.Empty, 1);
        var explorer = new LockingProcessInfo(11, "Windows 资源管理器", string.Empty, 4);
        var service = new LockingProcessInfo(12, "Search", "WSearch", 3);
        var critical = new LockingProcessInfo(13, "System", string.Empty, 1000);

        var plain = FileLockService.PlanRelease([word], currentProcessId: 1);
        Assert(plain.CanProceed && !plain.RestoreShell && plain.Message.Contains("Word"),
            "Ordinary application could not be released.");
        var withShell = FileLockService.PlanRelease([word, explorer, service, critical], currentProcessId: 1);
        Assert(withShell.CanProceed && withShell.RestoreShell &&
            withShell.Message.Contains("任务栏") && withShell.Message.Contains("不会自动重新启动") &&
            withShell.Message.Contains("无法关闭"),
            "Explorer, service or critical process was not called out before closing.");
        Assert(!FileLockService.PlanRelease([critical], currentProcessId: 1).CanProceed,
            "Release was offered although only critical processes hold the files.");
        Assert(!FileLockService.PlanRelease([word], currentProcessId: 10).CanProceed,
            "Release was offered although it would close DuoLaBox itself.");
    }

    // Shows the pick overlay for an instant; it takes no input and is closed again immediately.
    private static void VerifyWindowPickerLifecycle()
    {
        using var picker = new WindowPickerService();
        picker.Start();
        Assert(picker.IsActive, "Window picker did not start.");
        picker.Start();
        Assert(picker.IsActive, "Restarting the window picker left it inactive.");
        picker.Cancel();
        Assert(!picker.IsActive, "Window picker stayed active after cancel.");
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        process.WaitForExit();
        Assert(process.ExitCode == 0 && Directory.Exists(link), "Could not create the test junction.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
