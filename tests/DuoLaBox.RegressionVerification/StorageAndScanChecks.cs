using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using DuoLaBox.Models;
using DuoLaBox.Services;

internal static class StorageAndScanChecks
{
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DuoLaBoxStorage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            VerifyStagedBytes(directory);
            VerifyAtomicSave(directory);
            await VerifyScanAsync(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifyStagedBytes(string directory)
    {
        var path = Path.Combine(directory, "hosts-task.txt");
        File.WriteAllText(path, "127.0.0.1 localhost\r\n# 验证", new UTF8Encoding(true));
        var staged = StagedFile.Create(path);
        var bytes = staged.ReadVerifiedBytes();
        File.WriteAllText(path, "tampered");
        using (var reader = new StreamReader(new MemoryStream(bytes)))
        {
            Assert(reader.ReadToEnd() == "127.0.0.1 localhost\r\n# 验证",
                "Verified Hosts snapshot changed after the staging file was replaced.");
        }
        ExpectInvalidTask(staged);
        ExpectInvalidTask(new StagedFile(path, "not-a-hash"));
        ExpectInvalidTask(new StagedFile(path, "00"));

        var jsonPath = Path.Combine(directory, "cleanup-task.json");
        var candidatePath = Path.Combine(directory, "never-deleted.tmp");
        var items = new[] { new CleanupStagedItem(candidatePath, candidatePath) };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(items));
        var jsonTask = StagedFile.Create(jsonPath);
        var verifiedJson = jsonTask.ReadVerifiedBytes();
        File.WriteAllText(jsonPath, "[]");
        var snapshot = JsonSerializer.Deserialize<CleanupStagedItem[]>(verifiedJson)!;
        Assert(snapshot.Length == 1 && snapshot[0].Path == candidatePath,
            "Cleanup task parser did not retain the verified snapshot.");
        ExpectInvalidTask(jsonTask);
    }

    private static void ExpectInvalidTask(StagedFile staged)
    {
        try
        {
            staged.ReadVerifiedBytes();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Invalid task was accepted.");
    }

    private static void VerifyAtomicSave(string directory)
    {
        var path = Path.Combine(directory, "nested", "settings.json");
        AtomicFile.WriteAllText(path, "old");
        AtomicFile.WriteAllText(path, "新配置");
        Assert(File.ReadAllText(path) == "新配置" &&
               !File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }),
            "Atomic save changed text or added an unexpected UTF-8 BOM.");

        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ExpectSaveFailure(() => AtomicFile.WriteAllText(path, "must not replace locked target"));
        }
        Assert(File.ReadAllText(path) == "新配置", "Failed replacement damaged the original file.");
        ExpectSaveFailure(() => AtomicFile.WriteAllText(path, "must not overwrite", overwrite: false));
        Assert(File.ReadAllText(path) == "新配置", "Exclusive save overwrote the existing file.");

        try
        {
            AtomicFile.WriteAllText(path, "\uD800", new UTF8Encoding(false, true));
            throw new InvalidOperationException("Expected encoding failure did not occur.");
        }
        catch (EncoderFallbackException)
        {
            Assert(File.ReadAllText(path) == "新配置", "Encoding failure truncated the original file.");
        }
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".duolabox-*.tmp").Any(),
            "Atomic save left temporary files after success or failure.");
    }

    private static void ExpectSaveFailure(Action save)
    {
        try
        {
            save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
        throw new InvalidOperationException("Expected save failure did not occur.");
    }

    private static async Task VerifyScanAsync(string directory)
    {
        var scanDirectory = Path.Combine(directory, "scan");
        Directory.CreateDirectory(scanDirectory);
        var first = Path.Combine(scanDirectory, "first.tmp");
        File.WriteAllText(first, "safe test data");
        var sibling = Path.Combine(scanDirectory, "sibling.log");
        File.WriteAllText(sibling, "safe test data");
        var child = Path.Combine(scanDirectory, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "second.tmp"), "safe test data");
        for (var index = 0; index < 32; index++)
        {
            Directory.CreateDirectory(Path.Combine(child, $"empty-{index}", "nested"));
        }

        var service = new DiskCleanupService();
        var invalid = scanDirectory + "\\invalid\0name.tmp";
        var mixed = await service.ScanAsync(invalid + "\r\nrelative.tmp\r\n" + first);
        Assert(mixed.InvalidRuleCount == 2 && mixed.RecognizedRuleCount == 1 &&
               mixed.Candidates.Single().Path == first,
            "Invalid rules aborted the scan or were not classified correctly.");
        var recursiveRule = scanDirectory + Path.DirectorySeparatorChar;
        var recursive = await service.ScanAsync(recursiveRule);
        Assert(recursive.Candidates.Count == 3, "Directory rule did not scan nested files.");
        var multiple = await service.ScanAsync(first + "\r\n" + sibling);
        Assert(multiple.Candidates.Count == 2 &&
               multiple.Candidates.Any(item => item.Path == sibling),
            "An unmatched file was deduplicated before a later rule could select it.");
        var repeated = await service.ScanAsync(first + "\r\n" + recursiveRule);
        Assert(repeated.Candidates.Count == 3 &&
               repeated.Candidates.Count(item => item.Path == first) == 1,
            "Overlapping rules lost files or added duplicates.");
        var shallow = await service.ScanAsync(Path.Combine(scanDirectory, "*.tmp"));
        Assert(shallow.Candidates.Count == 1, "Single-level wildcard scanned child directories.");
        var root = Path.GetPathRoot(scanDirectory)!;
        Assert(CleanupRuleParser.TryPrepare(root, out var rootRule) && rootRule.Directory == root,
            "Drive root was converted to a drive-relative path.");
        Assert(CleanupRuleParser.TryPrepare(recursiveRule, out var prepared), "Test rule was rejected.");

        using var cancellation = new CancellationTokenSource();
        using (var files = CleanupRuleParser.EnumerateFiles(
                   prepared, cancellation.Token, Stopwatch.StartNew(), TimeSpan.FromMinutes(1)).GetEnumerator())
        {
            Assert(files.MoveNext(), "No file available for cancellation check.");
            cancellation.Cancel();
            try
            {
                files.MoveNext();
                throw new InvalidOperationException("Traversal swallowed cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
        }
        try
        {
            await service.ScanAsync(recursiveRule, cancellation.Token);
            throw new InvalidOperationException("Canceled scan returned a result.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert(!CleanupRuleParser.EnumerateFiles(
                prepared, CancellationToken.None, Stopwatch.StartNew(), TimeSpan.Zero).Any(),
            "Expired scan budget still enumerated files.");
        var emptyDirectory = Path.Combine(scanDirectory, "empty-only");
        Directory.CreateDirectory(emptyDirectory);
        Assert(CleanupRuleParser.TryPrepare(emptyDirectory + Path.DirectorySeparatorChar, out var emptyRule),
            "Empty-directory rule was rejected.");
        try
        {
            CleanupRuleParser.EnumerateFiles(
                emptyRule, cancellation.Token, Stopwatch.StartNew(), TimeSpan.FromMinutes(1)).Any();
            throw new InvalidOperationException("Empty-directory traversal ignored cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
