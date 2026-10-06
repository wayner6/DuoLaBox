using System.ComponentModel;
using System.Diagnostics;
using DuoLaBox.Models;
using DuoLaBox.Services;

namespace DuoLaBox;

public partial class MainWindow
{
    private async void DiskCleanupPage_ScanRequested()
    {
        _cleanupScanCancellation?.Cancel();
        _cleanupScanCancellation?.Dispose();
        var scanCancellation = new CancellationTokenSource();
        _cleanupScanCancellation = scanCancellation;
        var cancellationToken = scanCancellation.Token;
        bool IsCurrentScan() => !_exitRequested &&
            ReferenceEquals(_cleanupScanCancellation, scanCancellation);

        DiskCleanupPage.SetBusy(
            busy: true,
            "正在扫描安全缓存和日志，不会删除文件…");
        try
        {
            var library = _cleanupLibraryService.Load();
            var result = await _diskCleanupService.ScanAsync(
                library,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCurrentScan())
            {
                DiskCleanupPage.SetScanResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentScan())
            {
                DiskCleanupPage.SetBusy(busy: false, "扫描已取消。");
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentScan())
            {
                DiskCleanupPage.SetBusy(busy: false, $"扫描失败：{exception.Message}");
            }
        }
    }

    private async void DiskCleanupPage_CleanRequested(
        IReadOnlyList<CleanupCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        DiskCleanupPage.SetBusy(
            busy: true,
            "正在清理选中的文件…");
        CleanupExecutionResult directResult;
        try
        {
            directResult = await _diskCleanupService.ExecuteAsync(candidates);
        }
        catch (Exception exception)
        {
            DiskCleanupPage.SetBusy(busy: false, $"清理失败：{exception.Message}");
            return;
        }

        if (_diskCleanupService.IsAdministrator)
        {
            DiskCleanupPage.SetExecutionResult(directResult);
            return;
        }

        var administratorRetryPaths = new HashSet<string>(
            directResult.Failures?
                .Where(failure =>
                    failure.Kind == CleanupFailureKind.AccessDenied)
                .Select(failure => failure.Path) ?? [],
            StringComparer.OrdinalIgnoreCase);
        var administratorCandidates = candidates
            .Where(candidate =>
                administratorRetryPaths.Contains(candidate.Path))
            .ToArray();
        if (administratorCandidates.Length == 0)
        {
            DiskCleanupPage.SetExecutionResult(directResult);
            return;
        }

        StagedFile stagedFile;
        try
        {
            stagedFile = _diskCleanupService.Stage(
                administratorCandidates);
        }
        catch (Exception exception)
        {
            DiskCleanupPage.SetExecutionResult(
                directResult,
                $"受系统保护的项目未继续处理：{exception.Message}");
            return;
        }

        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException(
                    "无法确定当前程序路径。");
            }

            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            startInfo.ArgumentList.Add("--apply-cleanup");
            startInfo.ArgumentList.Add(stagedFile.Path);
            startInfo.ArgumentList.Add(stagedFile.Sha256);
            using var helper = Process.Start(startInfo);
            if (helper is null)
            {
                throw new InvalidOperationException(
                    "未能启动管理员清理程序。");
            }

            DiskCleanupPage.SetBusy(
                busy: true,
                "正在以管理员权限重试拒绝访问的项目；只有 Windows 启用了权限确认时才会显示确认窗口…");
            await helper.WaitForExitAsync();
            var administratorResult =
                _diskCleanupService.ReadStagedResult(stagedFile.Path);
            if (administratorResult is null)
            {
                DiskCleanupPage.SetExecutionResult(
                    directResult,
                    "普通权限可清理的文件已经处理；管理员清理未返回结果。系统若关闭了权限确认，不需要寻找弹窗。请重新扫描后再试。");
                return;
            }

            DiskCleanupPage.SetExecutionResult(
                MergeCleanupResults(
                    directResult,
                    administratorResult,
                    administratorRetryPaths),
                $"已用管理员权限重试 {administratorCandidates.Length} 个拒绝访问项目。仍保留的项目不会被强行取得所有权。");
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == 1223)
        {
            DiskCleanupPage.SetExecutionResult(
                directResult,
                "普通权限可清理的文件已经处理；受系统保护的项目因权限请求被取消而保留。");
        }
        catch (Exception exception)
        {
            DiskCleanupPage.SetExecutionResult(
                directResult,
                $"普通权限清理已经完成；受系统保护的项目未继续处理：{exception.Message}");
        }
        finally
        {
            _diskCleanupService.CleanupStagingFiles(stagedFile.Path);
        }
    }

    private static CleanupExecutionResult MergeCleanupResults(
        CleanupExecutionResult directResult,
        CleanupExecutionResult administratorResult,
        IReadOnlySet<string> administratorRetryPaths)
    {
        var retainedDirectFailures = (directResult.Failures ?? [])
            .Where(failure =>
                !administratorRetryPaths.Contains(failure.Path))
            .ToArray();
        var administratorFailures =
            administratorResult.Failures ?? [];
        var failures = retainedDirectFailures
            .Concat(administratorFailures)
            .ToArray();
        var failedPaths = failures
            .Select(failure => failure.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var deletedPaths = (directResult.DeletedPaths ?? [])
            .Concat(administratorResult.DeletedPaths ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CleanupExecutionResult(
            deletedPaths.Length,
            failedPaths.Length,
            directResult.ReleasedBytes +
                administratorResult.ReleasedBytes,
            deletedPaths,
            failedPaths,
            failures);
    }

    private void DiskCleanupPage_SaveLibraryRequested(
        string content)
    {
        if (!_cleanupLibraryService.Save(content))
        {
            DiskCleanupPage.SetLibraryStatus(
                "清理库保存失败，请检查本地配置目录权限。");
            return;
        }

        DiskCleanupPage.LoadLibrary(
            _cleanupLibraryService.Load(),
            $"已保存：{_cleanupLibraryService.CustomLibraryPath}");
    }

    private void DiskCleanupPage_RestoreLibraryRequested()
    {
        var defaultLibrary =
            _cleanupLibraryService.LoadDefault();
        if (!_cleanupLibraryService.Save(defaultLibrary))
        {
            DiskCleanupPage.SetLibraryStatus(
                "恢复默认清理库失败。");
            return;
        }

        DiskCleanupPage.LoadLibrary(
            defaultLibrary,
            "已恢复并保存默认 FileList.csv 清理库。");
    }

    private void DiskCleanupPage_ImportLibraryRequested(string path)
    {
        try
        {
            var content = _cleanupLibraryService.Import(path);
            DiskCleanupPage.LoadLibrary(
                content,
                "已读取 CSV；点击“保存清理库”后生效。");
        }
        catch (Exception exception)
        {
            DiskCleanupPage.SetLibraryStatus(
                $"读取失败：{exception.Message}");
        }
    }
}
