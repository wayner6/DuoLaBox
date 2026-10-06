using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using DuoLaBox.Models;

namespace DuoLaBox.Services;

public sealed class DiskCleanupService
{
    private const int CandidateLimit = 20000;
    private const int DeleteAttemptCount = 3;
    private static readonly TimeSpan ScanTimeLimit =
        TimeSpan.FromSeconds(20);

    private readonly string _stagingDirectory;

    public DiskCleanupService()
    {
        _stagingDirectory =
            AppIdentity.GetLocalDataPath("CleanupStaging");
    }

    public bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(
                    WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<CleanupScanResult> ScanAsync(
        string library,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Scan(library, cancellationToken),
            cancellationToken);
    }

    public Task<CleanupExecutionResult> ExecuteAsync(
        IReadOnlyList<CleanupCandidate> candidates)
    {
        var items = candidates.Select(item =>
            new CleanupStagedItem(item.Path, item.Rule)).ToArray();
        return Task.Run(() => Execute(items));
    }

    public StagedFile Stage(
        IEnumerable<CleanupCandidate> candidates)
    {
        Directory.CreateDirectory(_stagingDirectory);
        var path = Path.Combine(
            _stagingDirectory,
            $"cleanup-{Guid.NewGuid():N}.json");
        var items = candidates
            .Where(item => item.IsSelected)
            .Select(item => new CleanupStagedItem(
                item.Path,
                item.Rule))
            .ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(items));
        return StagedFile.Create(path);
    }

    public CleanupExecutionResult ExecuteStaged(
        string stagingPath,
        string expectedHash)
    {
        var fullPath = ValidateStagingPath(stagingPath);
        var bytes = new StagedFile(fullPath, expectedHash).ReadVerifiedBytes();
        var items = JsonSerializer.Deserialize<CleanupStagedItem[]>(bytes) ?? [];
        var result = Execute(items);
        File.WriteAllText(
            GetResultPath(fullPath),
            JsonSerializer.Serialize(result));
        return result;
    }

    public CleanupExecutionResult? ReadStagedResult(
        string stagingPath)
    {
        try
        {
            var fullPath = ValidateStagingPath(stagingPath);
            var resultPath = GetResultPath(fullPath);
            if (!File.Exists(resultPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CleanupExecutionResult>(
                File.ReadAllText(resultPath));
        }
        catch
        {
            return null;
        }
    }

    public void CleanupStagingFiles(string stagingPath)
    {
        TryDelete(stagingPath);
        TryDelete(GetResultPath(stagingPath));
    }

    private static CleanupScanResult Scan(
        string library,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var rules = CleanupRuleParser.Parse(library, cancellationToken);
        var candidates = new List<CleanupCandidate>();
        var uniquePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var recognizedRuleCount = 0;
        var invalidRuleCount = 0;
        var inaccessibleCount = 0;
        var limitReached = false;

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= ScanTimeLimit)
            {
                limitReached = true;
                break;
            }

            if (!CleanupRuleParser.TryPrepare(rule.Path, out var prepared))
            {
                invalidRuleCount++;
                continue;
            }

            recognizedRuleCount++;
            try
            {
                foreach (var file in CleanupRuleParser.EnumerateFiles(
                             prepared, cancellationToken, stopwatch, ScanTimeLimit))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (candidates.Count >= CandidateLimit ||
                        stopwatch.Elapsed >= ScanTimeLimit)
                    {
                        limitReached = true;
                        break;
                    }

                    try
                    {
                        var fullPath = Path.GetFullPath(file);
                        if (uniquePaths.Contains(fullPath) ||
                            !CleanupRuleParser.IsFileAllowed(
                                fullPath,
                                prepared))
                        {
                            continue;
                        }

                        var info = new FileInfo(fullPath);
                        if (!info.Exists ||
                            info.Attributes.HasFlag(
                                FileAttributes.ReparsePoint))
                        {
                            continue;
                        }

                        var size = info.Length;
                        uniquePaths.Add(fullPath);
                        candidates.Add(new CleanupCandidate
                        {
                            Path = fullPath,
                            Rule = rule.Path,
                            Category = rule.Category,
                            Description = rule.Description,
                            Size = size
                        });
                    }
                    catch
                    {
                        inaccessibleCount++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                inaccessibleCount++;
            }

            limitReached |= stopwatch.Elapsed >= ScanTimeLimit;
            if (limitReached)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new CleanupScanResult(
            candidates,
            rules.Count,
            recognizedRuleCount,
            invalidRuleCount,
            inaccessibleCount,
            limitReached);
    }

    private static CleanupExecutionResult Execute(
        IEnumerable<CleanupStagedItem> items)
    {
        var deleted = 0;
        var failed = 0;
        long released = 0;
        var deletedPaths = new List<string>();
        var failedPaths = new List<string>();
        var failures = new List<CleanupFailure>();
        foreach (var item in items)
        {
            try
            {
                if (!CleanupRuleParser.TryPrepare(item.Rule, out var prepared))
                {
                    failed++;
                    failedPaths.Add(item.Path);
                    failures.Add(new CleanupFailure(
                        item.Path,
                        CleanupFailureKind.InvalidRule));
                    continue;
                }

                var fullPath = Path.GetFullPath(item.Path);
                if (!CleanupRuleParser.IsFileAllowed(
                        fullPath,
                        prepared))
                {
                    failed++;
                    failedPaths.Add(fullPath);
                    failures.Add(new CleanupFailure(
                        fullPath,
                        CleanupFailureKind.ValidationRejected));
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    deleted++;
                    deletedPaths.Add(fullPath);
                    continue;
                }

                var info = new FileInfo(fullPath);
                var length = info.Length;
                Exception? deleteException = null;
                for (var attempt = 0;
                     attempt < DeleteAttemptCount;
                     attempt++)
                {
                    try
                    {
                        if (info.IsReadOnly)
                        {
                            info.IsReadOnly = false;
                        }

                        File.Delete(fullPath);
                        deleteException = null;
                        break;
                    }
                    catch (Exception exception)
                    {
                        if (!File.Exists(fullPath))
                        {
                            deleteException = null;
                            break;
                        }

                        deleteException = exception;
                        if (attempt + 1 < DeleteAttemptCount &&
                            IsTransientDeleteFailure(exception))
                        {
                            Thread.Sleep(70 * (attempt + 1));
                            continue;
                        }

                        break;
                    }
                }

                if (deleteException is not null)
                {
                    failed++;
                    failedPaths.Add(fullPath);
                    failures.Add(new CleanupFailure(
                        fullPath,
                        ClassifyDeleteFailure(deleteException)));
                    continue;
                }

                deleted++;
                released += length;
                deletedPaths.Add(fullPath);
            }
            catch (Exception exception)
            {
                failed++;
                failedPaths.Add(item.Path);
                failures.Add(new CleanupFailure(
                    item.Path,
                    ClassifyDeleteFailure(exception)));
            }
        }

        return new CleanupExecutionResult(
            deleted,
            failed,
            released,
            deletedPaths,
            failedPaths,
            failures);
    }

    private static bool IsTransientDeleteFailure(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return false;
        }

        var error = exception.HResult & 0xFFFF;
        return error is 32 or 33;
    }

    private static CleanupFailureKind ClassifyDeleteFailure(
        Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return CleanupFailureKind.AccessDenied;
        }

        var error = exception.HResult & 0xFFFF;
        return error switch
        {
            5 => CleanupFailureKind.AccessDenied,
            32 or 33 => CleanupFailureKind.InUse,
            _ => CleanupFailureKind.Other
        };
    }


    private string ValidateStagingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetFullPath(_stagingDirectory)
            .TrimEnd('\\') + "\\";
        if (!fullPath.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "拒绝了不受信任的清理任务路径。");
        }

        return fullPath;
    }

    private static string GetResultPath(string path) =>
        path + ".result";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理临时文件失败不影响操作结果。
        }
    }

}
