using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using DuoLaBox.Models;
using DuoLaBox.Services;
using DuoLaBox.Views;

// Behaviour fixes that can be checked without touching system settings. Works only inside a new temp directory.
internal static class BehaviourChecks
{
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DuoLaBoxBehaviour-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await VerifyCaseOnlyRenameAsync(directory);
            await VerifyImageResizeAsync(directory);
            await VerifyQueuedShellRequestsAsync(directory);
            VerifyGlobPruning(directory);
            VerifyHostsEncoding();
            VerifySchedulePeriods();
            VerifyTrayMenuPlacement();
            Assert(new AppSettings() is
                {
                    RenameContextMenuEnabled: false,
                    UnlockContextMenuEnabled: false,
                    ResizeContextMenuEnabled: false,
                    StartWithWindows: false
                },
                "A fresh install would write to the registry without being asked.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task VerifyCaseOnlyRenameAsync(string directory)
    {
        var folder = Path.Combine(directory, "case");
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "report.txt");
        File.WriteAllText(source, "content");
        var service = new BatchRenameService();
        var plan = service.CreatePlan([source], "REPORT{ext}", 1);
        await service.ExecuteAsync(plan);
        Assert(Directory.GetFiles(folder).Select(Path.GetFileName).Single() == "REPORT.txt" &&
            File.ReadAllText(Path.Combine(folder, "REPORT.txt")) == "content",
            "A rename that only changes letter case was skipped.");
    }

    private static async Task VerifyImageResizeAsync(string directory)
    {
        var folder = Path.Combine(directory, "images");
        Directory.CreateDirectory(folder);

        // Stored 40x20 with the left half red; orientation 6 means "rotate 90° clockwise to display",
        // so the upright picture is 20x40 with red on top and blue below.
        var portrait = Path.Combine(folder, "portrait.jpg");
        using (var bitmap = new Bitmap(40, 20))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.FillRectangle(Brushes.Red, 0, 0, 20, 20);
                graphics.FillRectangle(Brushes.Blue, 20, 0, 20, 20);
            }
            var orientation = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
            orientation.Id = 0x0112;
            orientation.Type = 3;
            orientation.Len = 2;
            orientation.Value = BitConverter.GetBytes((ushort)6);
            bitmap.SetPropertyItem(orientation);
            bitmap.Save(portrait, ImageFormat.Jpeg);
        }
        var broken = Path.Combine(folder, "broken.jpg");
        File.WriteAllText(broken, "this is not an image");

        var result = await new ImageResizeService().ResizeAsync([broken, portrait], 20, 40, preserveAspectRatio: false);
        Assert(result.Outputs.Count == 1 && result.Failures.Single().Path == broken,
            "One unreadable image stopped the batch or was not reported.");
        Assert(Directory.GetFiles(Path.GetDirectoryName(result.Outputs[0])!).Length == 1,
            "A failed image left an output file behind.");
        using (var output = new Bitmap(result.Outputs[0]))
        {
            var top = output.GetPixel(5, 5);
            var bottom = output.GetPixel(5, 35);
            Assert(output.Width == 20 && output.Height == 40 &&
                top.R > 180 && top.B < 90 && bottom.B > 180 && bottom.R < 90,
                $"Portrait photo was not turned upright: top={top}, bottom={bottom}.");
            Assert(!output.PropertyIdList.Contains(0x0112), "Output kept an orientation tag that no longer applies.");
        }

        // The preview reads size and orientation without decoding the full picture.
        var portraitPreview = FileBatchPage.LoadPreview(portrait);
        Assert(portraitPreview is { Width: 20, Height: 40 } &&
            portraitPreview.Image.PixelWidth == 20 && portraitPreview.Image.PixelHeight == 40,
            "Preview of a portrait photo is not upright.");
        var large = Path.Combine(folder, "large.png");
        using (var bitmap = new Bitmap(2000, 1000))
        {
            bitmap.Save(large, ImageFormat.Png);
        }
        var largePreview = FileBatchPage.LoadPreview(large);
        Assert(largePreview is { Width: 2000, Height: 1000 } && largePreview.Image.PixelWidth == 640,
            "Preview decoded a large picture at full size or lost its real dimensions.");

        Assert(ImageResizeService.GetOrientationFlip(1) == RotateFlipType.RotateNoneFlipNone &&
            ImageResizeService.GetOrientationFlip(8) == RotateFlipType.Rotate270FlipNone &&
            FileBatchPage.GetOrientationTransform(1) is null &&
            FileBatchPage.GetOrientationTransform(6) is not null,
            "Orientation mapping is inconsistent.");
    }

    // Explorer starts one process per selected file; every one of them must arrive, in one batch.
    private static async Task VerifyQueuedShellRequestsAsync(string directory)
    {
        var folder = Path.Combine(directory, "queue");
        Directory.CreateDirectory(folder);
        var files = Enumerable.Range(0, 100).Select(index => Path.Combine(folder, $"file-{index:000}.txt")).ToArray();
        foreach (var file in files)
        {
            File.WriteAllText(file, "queued");
        }

        var page = new FileBatchPage();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = page.RunBusyAsync(() => release.Task);
        foreach (var file in files)
        {
            page.Open(FileToolMode.Rename, [file]);
        }
        release.SetResult(true);
        await active;
        var list = (System.Windows.Controls.ListBox)page.FindName("SelectedFilesList");
        Assert(list.Items.Count == files.Length, $"Only {list.Items.Count} of {files.Length} queued files arrived.");
    }

    // Wildcard rules visit only directories that can lead to a match, and still find exactly
    // what a full walk filtered by the rule's pattern finds.
    private static void VerifyGlobPruning(string directory)
    {
        var root = Path.Combine(directory, "glob");
        string[] relative =
        [
            @"a\Cache\x.tmp", @"a\Cache\deep\z.tmp", @"a\Other\n.tmp", @"a\Cache2\q.tmp",
            @"b\Cache\y.log", @"long name\Cache\w.tmp", "top.tmp"
        ];
        foreach (var item in relative)
        {
            var file = Path.Combine(root, item);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "safe test data");
        }
        var all = Directory.GetFiles(root, "*", SearchOption.AllDirectories);

        string[] Walk(string pattern, out PreparedCleanupRule rule)
        {
            Assert(CleanupRuleParser.TryPrepare(Path.Combine(root, pattern), out rule), $"Rule rejected: {pattern}");
            return [.. CleanupRuleParser.EnumerateFiles(
                rule, CancellationToken.None, System.Diagnostics.Stopwatch.StartNew(), TimeSpan.FromMinutes(1))];
        }

        var expectations = new Dictionary<string, int>
        {
            [@"*\Cache\**"] = 4,
            [@"*\Cache\*.tmp"] = 2,
            [@"**\*.tmp"] = 5,
            [@"?\Cache\*.log"] = 1,
            [@"*.tmp"] = 1,
            [@"a\**"] = 4,
            [@"*\Cache*\*.tmp"] = 3
        };
        foreach (var (pattern, expected) in expectations)
        {
            var visited = Walk(pattern, out var rule);
            var matched = visited.Where(file => CleanupRuleParser.IsFileAllowed(file, rule))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var reference = all.Where(file => CleanupRuleParser.IsFileAllowed(file, rule))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert(matched.SequenceEqual(reference, StringComparer.OrdinalIgnoreCase) && matched.Length == expected,
                $"Rule {pattern} matched {matched.Length} files, full walk matched {reference.Length}, expected {expected}.");
        }

        var pruned = Walk(@"*\Cache\*.tmp", out _).Select(file => Path.GetRelativePath(root, file)).ToArray();
        Assert(pruned.Length == 3 && pruned.All(file => Path.GetFileName(Path.GetDirectoryName(file)) == "Cache"),
            "Wildcard rule walked directories that cannot match: " + string.Join(", ", pruned));
    }

    private static void VerifyHostsEncoding()
    {
        const string text = "# 本地开发环境\r\n127.0.0.1 example.test\r\n";
        var utf8 = new UTF8Encoding(false).GetBytes(text);
        var detectedUtf8 = HostsFileService.DetectEncoding(utf8);
        Assert(detectedUtf8 is UTF8Encoding && HostsFileService.Decode(utf8, detectedUtf8) == text &&
            HostsFileService.Encode(text, detectedUtf8).SequenceEqual(utf8),
            "UTF-8 Hosts content did not round-trip.");
        var withBom = Encoding.UTF8.GetPreamble().Concat(utf8).ToArray();
        Assert(HostsFileService.Decode(withBom, HostsFileService.DetectEncoding(withBom)) == text,
            "UTF-8 byte order mark leaked into the Hosts text.");

        // A file saved by old Notepad in the local code page: whatever the test machine's code page is,
        // reading and saving without edits must give back the same bytes.
        var gbk = Encoding.GetEncoding(936);
        var legacy = gbk.GetBytes(text);
        var detectedLegacy = HostsFileService.DetectEncoding(legacy);
        Assert(detectedLegacy is not UTF8Encoding &&
            HostsFileService.Encode(HostsFileService.Decode(legacy, detectedLegacy), detectedLegacy).SequenceEqual(legacy),
            "A Hosts file in the local code page was altered by reading and saving it.");
        const string emoji = "# \U0001F600\r\n";
        Assert(HostsFileService.Encode(emoji, gbk).SequenceEqual(new UTF8Encoding(false).GetBytes(emoji)),
            "Characters the old code page cannot hold were not saved as UTF-8.");

        var backups = Enumerable.Range(1, 12)
            .Select(day => $@"C:\etc\hosts.DuoLaBox备份-202611{day:00}-101010")
            .Append(@"C:\etc\hosts.DuoLaBox备份-20261112-101010-500")
            .ToArray();
        var pruned = HostsFileService.SelectBackupsToPrune(backups.Reverse()).ToArray();
        Assert(pruned.Length == 3 && pruned.All(path => path.Contains("-2026110")) &&
            !pruned.Any(path => path.EndsWith("-500", StringComparison.Ordinal)),
            "Backup pruning did not keep the ten most recent backups.");
    }

    private static void VerifySchedulePeriods()
    {
        var evening = new TimeSpan(20, 0, 0);
        var morning = new TimeSpan(7, 0, 0);
        Assert(DayNightScheduleService.IsWithinDarkPeriod(new TimeSpan(23, 0, 0), evening, morning) &&
            DayNightScheduleService.IsWithinDarkPeriod(new TimeSpan(6, 59, 0), evening, morning) &&
            !DayNightScheduleService.IsWithinDarkPeriod(morning, evening, morning) &&
            !DayNightScheduleService.IsWithinDarkPeriod(new TimeSpan(12, 0, 0), evening, morning) &&
            DayNightScheduleService.IsWithinDarkPeriod(new TimeSpan(12, 0, 0), morning, evening) &&
            !DayNightScheduleService.IsWithinDarkPeriod(new TimeSpan(12, 0, 0), morning, morning),
            "Dark period boundaries are wrong.");
    }

    private static void VerifyTrayMenuPlacement()
    {
        var work = new Rectangle(1920, 0, 2560, 1400);
        var open = TrayMenuWindow.CalculatePosition(new Point(2000, 100), work, 300, 200, 10);
        Assert(open == (2010, 110), "Tray menu did not open beside the pointer.");
        var corner = TrayMenuWindow.CalculatePosition(new Point(4470, 1390), work, 300, 200, 10);
        Assert(corner == (4160, 1180) && corner.X >= work.Left && corner.X + 300 <= work.Right &&
            corner.Y + 200 <= work.Bottom,
            "Tray menu near the screen corner ran off the monitor.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
