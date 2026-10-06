using System.IO;
using DuoLaBox.Models;

namespace DuoLaBox.Services;

public sealed class BatchRenameService
{
    public IReadOnlyList<RenamePreviewItem> CreatePlan(
        IEnumerable<string> paths,
        string template,
        int startIndex)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException("重命名模板不能为空。");
        }

        var files = paths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var plan = new List<RenamePreviewItem>(files.Length);

        for (var offset = 0; offset < files.Length; offset++)
        {
            var source = Path.GetFullPath(files[offset]);
            var extension = Path.GetExtension(source);
            var originalName = Path.GetFileNameWithoutExtension(source);
            var destinationName = template
                .Replace("{name}", originalName, StringComparison.OrdinalIgnoreCase)
                .Replace("{index}", (startIndex + offset).ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{ext}", extension, StringComparison.OrdinalIgnoreCase);

            if (!template.Contains(
                    "{ext}",
                    StringComparison.OrdinalIgnoreCase))
            {
                destinationName += extension;
            }

            ValidateFileName(destinationName);
            var directory = Path.GetDirectoryName(source) ??
                            throw new InvalidOperationException(
                                "无法确定源文件目录。");
            plan.Add(new RenamePreviewItem(
                source,
                Path.Combine(directory, destinationName)));
        }

        ValidatePlan(plan);
        return plan;
    }

    public Task ExecuteAsync(
        IReadOnlyList<RenamePreviewItem> plan)
    {
        var snapshot = plan.ToArray();
        return Task.Run(() => Execute(snapshot));
    }

    private static void Execute(
        IReadOnlyList<RenamePreviewItem> plan)
    {
        var changes = plan
            .Where(item => !string.Equals(
                item.SourcePath,
                item.DestinationPath,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => new StagedRename(
                item.SourcePath,
                Path.Combine(
                    Path.GetDirectoryName(item.SourcePath)!,
                    $".duolabox-{Guid.NewGuid():N}.tmp"),
                item.DestinationPath))
            .ToArray();

        var stagedCount = 0;
        var completedCount = 0;
        try
        {
            foreach (var change in changes)
            {
                File.Move(change.Source, change.Temporary);
                stagedCount++;
            }

            foreach (var change in changes)
            {
                File.Move(change.Temporary, change.Destination);
                completedCount++;
            }
        }
        catch (Exception exception)
        {
            var returnedToTemporary = new bool[completedCount];
            var unrecoveredPaths = new List<string>();
            // Free overlapping destination names before restoring any source name.
            for (var index = completedCount - 1; index >= 0; index--)
            {
                returnedToTemporary[index] = TryMove(
                    changes[index].Destination,
                    changes[index].Temporary);
                if (!returnedToTemporary[index])
                {
                    unrecoveredPaths.Add(changes[index].Destination);
                }
            }

            for (var index = stagedCount - 1; index >= 0; index--)
            {
                if (index < completedCount && !returnedToTemporary[index])
                {
                    continue;
                }

                if (!TryMove(changes[index].Temporary, changes[index].Source))
                {
                    unrecoveredPaths.Add(changes[index].Temporary);
                }
            }

            if (unrecoveredPaths.Count > 0)
            {
                throw new IOException(
                    "重命名失败，部分文件未能恢复原文件名。请检查以下路径：" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, unrecoveredPaths),
                    exception);
            }

            throw;
        }
    }

    private static void ValidatePlan(
        IReadOnlyList<RenamePreviewItem> plan)
    {
        var sourcePaths = plan
            .Select(item => item.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicate = plan
            .GroupBy(
                item => item.DestinationPath,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"多个文件将被命名为“{Path.GetFileName(duplicate.Key)}”。");
        }

        foreach (var item in plan)
        {
            if (File.Exists(item.DestinationPath) &&
                !sourcePaths.Contains(item.DestinationPath))
            {
                throw new IOException(
                    $"目标文件已存在：{item.DestinationPath}");
            }
        }
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException(
                $"文件名无效：{fileName}");
        }
    }

    private static bool TryMove(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record StagedRename(
        string Source,
        string Temporary,
        string Destination);
}
