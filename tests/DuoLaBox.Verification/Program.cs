using System.Diagnostics;
using System.Drawing;
using System.IO;
using DuoLaBox;
using DuoLaBox.Models;
using DuoLaBox.Services;

Assert(AppIdentity.Name == "DuoLaBox", "应用内部标识未统一");
Assert(
    AppIdentity.LocalDataDirectory.EndsWith(
        Path.DirectorySeparatorChar + "DuoLaBox",
        StringComparison.OrdinalIgnoreCase),
    "应用数据目录未迁移到 DuoLaBox");

var testDirectory = Path.Combine(
    Path.GetTempPath(),
    $"DuoLaBoxVerification-{Guid.NewGuid():N}");
Directory.CreateDirectory(testDirectory);

try
{
    Console.WriteLine("VERIFY: file tools");
    var first = Path.Combine(testDirectory, "alpha.txt");
    var second = Path.Combine(testDirectory, "beta.txt");
    File.WriteAllText(first, "alpha");
    File.WriteAllText(second, "beta");

    var stagedPath = Path.Combine(testDirectory, "staged-task.json");
    File.WriteAllText(stagedPath, "trusted");
    var stagedFile = StagedFile.Create(stagedPath);
    Assert(stagedFile.HasExpectedHash(), "临时任务完整性校验失败");
    File.WriteAllText(stagedPath, "tampered");
    Assert(!stagedFile.HasExpectedHash(), "临时任务篡改未被识别");

    var renameService = new BatchRenameService();
    var plan = renameService.CreatePlan(
        [first, second],
        "文档_{index}{ext}",
        7);
    await renameService.ExecuteAsync(plan);
    Assert(
        File.Exists(Path.Combine(testDirectory, "文档_7.txt")) &&
        File.Exists(Path.Combine(testDirectory, "文档_8.txt")),
        "批量重命名结果不正确");

    var imagePath = Path.Combine(testDirectory, "sample.png");
    using (var image = new Bitmap(100, 50))
    {
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.CornflowerBlue);
        image.Save(imagePath);
    }

    var resizeService = new ImageResizeService();
    var outputs = await resizeService.ResizeAsync(
        [imagePath],
        200,
        200,
        preserveAspectRatio: true);
    using (var resized = Image.FromFile(outputs.Single()))
    {
        Assert(
            resized.Width == 200 && resized.Height == 200,
            "图片输出尺寸不正确");
    }

    var secondOutputs = await resizeService.ResizeAsync(
        [imagePath],
        200,
        200,
        preserveAspectRatio: false);
    Assert(
        secondOutputs.Single() != outputs.Single(),
        "重复处理图片时覆盖了已有输出");

    var favoriteColor = new FavoriteColorItem("#1689D8");
    Assert(
        favoriteColor.Hex == "#1689D8" &&
        favoriteColor.Rgb == "rgb(22, 137, 216)" &&
        favoriteColor.Hsl.StartsWith("hsl(") &&
        favoriteColor.Hsv.StartsWith("hsv("),
        "收藏颜色未生成全部复制格式");
    var sortableCandidates = new[]
    {
        new CleanupCandidate
        {
            Path = @"C:\Temp\zeta.tmp",
            Rule = @"C:\Temp\*.*",
            Size = 10
        },
        new CleanupCandidate
        {
            Path = @"C:\Temp\alpha.tmp",
            Rule = @"C:\Temp\*.*",
            Size = 100
        }
    };
    Assert(
        CleanupCandidateSorter.Sort(
            sortableCandidates,
            CleanupSortMode.Name)[0].Path.EndsWith("alpha.tmp") &&
        CleanupCandidateSorter.Sort(
            sortableCandidates,
            CleanupSortMode.FileSize)[0].Size == 100,
        "清理列表排序结果不正确");
    Console.WriteLine("VERIFY: system information");
    var systemInformation = await new SystemInformationService()
        .ScanAsync();
    Assert(
        !string.IsNullOrWhiteSpace(
            systemInformation.ModelSummary) &&
        !string.IsNullOrWhiteSpace(
            systemInformation.OperatingSystemSummary) &&
        systemInformation.Groups.Count == 8 &&
        systemInformation.Groups.All(group =>
            group.Values.Count > 0),
        "System information scan returned incomplete data");

    Console.WriteLine("VERIFY: file locks");
    var lockedPath = Path.Combine(testDirectory, "locked.txt");
    File.WriteAllText(lockedPath, "locked");
    try
    {
        await using var lockedStream = new FileStream(
            lockedPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var locks = await new FileLockService()
            .GetLockingProcessesAsync([lockedPath]);
        Assert(
            locks.Any(item =>
                item.ProcessId == Environment.ProcessId),
            "未检测到当前进程持有的文件锁");
    }
    catch (InvalidOperationException exception)
        when (exception.Message.Contains("错误 29"))
    {
        Console.WriteLine(
            "RESTART_MANAGER_UNAVAILABLE_ON_HOST: ERROR_WRITE_FAULT");
    }

    Console.WriteLine("VERIFY: context menu");
    var shellMenu = new ShellContextMenuService();
    foreach (var mode in Enum.GetValues<FileToolMode>())
    {
        var configured = shellMenu.Configure(mode, enabled: true);
        if (!configured &&
            shellMenu.LastErrorMessage?.Contains(
                "denied",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            Console.WriteLine(
                "CONTEXT_MENU_SKIPPED: registry access denied by host");
            break;
        }

        Assert(
            configured,
            $"右键菜单注册失败：{shellMenu.LastErrorMessage}");
    }

    Console.WriteLine("VERIFY: cleanup library");
    var cleanupLibrary = new CleanupLibraryService().LoadDefault();
    Assert(
        cleanupLibrary.Contains("# DuoLaBoxCleanupLibraryVersion=2") &&
        cleanupLibrary.Contains("%SystemRoot%\\Temp\\") &&
        cleanupLibrary.Contains("%TEMP%\\") &&
        cleanupLibrary.Contains(
            "# [Windows 临时文件]") &&
        cleanupLibrary.Contains(
            "Microsoft\\Windows\\WER\\ReportArchive\\") &&
        cleanupLibrary.Contains(
            "%LOCALAPPDATA%\\CrashDumps\\*.dmp") &&
        !cleanupLibrary.Contains("Windows\\Installer") &&
        !cleanupLibrary.Contains("Windows\\WinSxS"),
        "默认清理库未正确读取或缺少新增规则");
    var cleanupRuleCount = cleanupLibrary
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line =>
            line.Length > 0 &&
            !line.Equals(
                "文件名",
                StringComparison.OrdinalIgnoreCase) &&
            !line.StartsWith('#'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    Assert(
        cleanupRuleCount == 64,
        $"默认清理库规则数量异常：{cleanupRuleCount}");

    Console.WriteLine("VERIFY: cleanup execution");
    var cleanupDirectory = Path.Combine(
        Path.GetTempPath(),
        $"DuoLaBoxCleanupVerification-{Guid.NewGuid():N}");
    Directory.CreateDirectory(cleanupDirectory);
    try
    {
        var cleanupFile = Path.Combine(
            cleanupDirectory,
            "current-cache.tmp");
        File.WriteAllText(cleanupFile, "cleanup");

        var cleanupService = new DiskCleanupService();
        var rootRuleScan = await cleanupService.ScanAsync(
            "C:\\*.sys\r\nC:\\bootTel.dat\r\n" +
            "D:\\DuoLaBoxRuleVerification\\*.tmp");
        Assert(
            rootRuleScan.RecognizedRuleCount == 3 &&
            rootRuleScan.InvalidRuleCount == 0,
            "根目录或其他本地盘规则未被直接采用");

        var cleanupScan = await cleanupService.ScanAsync(cleanupFile);
        Assert(
            cleanupScan.Candidates.Count == 1 &&
            cleanupScan.InvalidRuleCount == 0,
            "安全临时文件未进入清理预览");
        var cleanupResult = await cleanupService.ExecuteAsync(
            cleanupScan.Candidates);
        Assert(
            cleanupResult.DeletedCount == 1 &&
            cleanupResult.DeletedPaths?.Contains(cleanupFile) == true &&
            !File.Exists(cleanupFile),
            "清理库中的文件清理失败");

        var occupiedCleanupFile = Path.Combine(
            cleanupDirectory,
            "occupied-cache.tmp");
        File.WriteAllText(occupiedCleanupFile, "occupied");
        await using (var occupiedStream = new FileStream(
                         occupiedCleanupFile,
                         FileMode.Open,
                         FileAccess.ReadWrite,
                         FileShare.None))
        {
            var occupiedScan = await cleanupService.ScanAsync(
                occupiedCleanupFile);
            var occupiedResult = await cleanupService.ExecuteAsync(
                occupiedScan.Candidates);
            Assert(
                occupiedResult.FailedCount == 1 &&
                occupiedResult.Failures?.Single().Kind ==
                    CleanupFailureKind.InUse &&
                File.Exists(occupiedCleanupFile),
                "被占用文件未正确归类为正在使用");
        }
        File.Delete(occupiedCleanupFile);

        var unrestrictedDirectory = Path.Combine(
            cleanupDirectory,
            "Windows",
            "Installer");
        Directory.CreateDirectory(unrestrictedDirectory);
        var unrestrictedFile = Path.Combine(
            unrestrictedDirectory,
            "direct-rule.sys");
        File.WriteAllText(unrestrictedFile, "direct");
        var unrestrictedScan = await cleanupService.ScanAsync(
            unrestrictedFile);
        Assert(
            unrestrictedScan.Candidates.Count == 1 &&
            unrestrictedScan.RecognizedRuleCount == 1 &&
            unrestrictedScan.InvalidRuleCount == 0,
            "有效清理规则仍被路径或扩展名策略阻止");
    }
    finally
    {
        Directory.Delete(cleanupDirectory, recursive: true);
    }

    Console.WriteLine("VERIFICATION_OK");
}
finally
{
    Directory.Delete(testDirectory, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
