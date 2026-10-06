using System.Windows;
using DuoLaBox.Models;
using MessageBox = System.Windows.MessageBox;

namespace DuoLaBox.Views;

public partial class FileBatchPage
{
    private async void AnalyzeLocksButton_Click(object sender, RoutedEventArgs e) =>
        await AnalyzeLocksAsync();

    private async Task AnalyzeLocksAsync()
    {
        await RunBusyAsync(async () =>
        {
            var files = _unlockFiles.ToArray();
            var processes = await _fileLockService.GetLockingProcessesAsync(files);
            UpdateLockingProcesses(processes);
            StatusText.Text = processes.Count == 0
                ? "未检测到占用这些文件的程序。"
                : $"检测到 {processes.Count} 个占用程序。";
        });
    }

    private async void ReleaseLocksButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var files = _unlockFiles.ToArray();
            var processes = await _fileLockService.GetLockingProcessesAsync(files);
            UpdateLockingProcesses(processes);
            if (processes.Count == 0)
            {
                StatusText.Text = "文件当前没有被其他程序占用。";
                return;
            }

            var names = string.Join("、", processes.Select(process => process.ApplicationName));
            if (MessageBox.Show(
                    Window.GetWindow(this),
                    $"将请求以下程序关闭以解除占用：\n\n{names}\n\n" +
                    "请先保存这些程序中的工作。是否继续？",
                    "确认解除文件占用",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var success = await _fileLockService.ReleaseLocksAsync(files);
            var remaining = await _fileLockService.GetLockingProcessesAsync(files);
            UpdateLockingProcesses(remaining);
            StatusText.Text = success && remaining.Count == 0
                ? "已解除文件占用。"
                : "部分程序未能正常关闭，文件可能仍被占用。";
            if (remaining.Count == 0 ||
                MessageBox.Show(
                    Window.GetWindow(this),
                    "仍有程序占用文件。是否强制结束这些程序？\n\n" +
                    "强制结束可能导致这些程序中未保存的内容丢失。",
                    "强制解除文件占用",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await _fileLockService.ReleaseLocksAsync(files, force: true);
            remaining = await _fileLockService.GetLockingProcessesAsync(files);
            UpdateLockingProcesses(remaining);
            StatusText.Text = remaining.Count == 0
                ? "已强制解除文件占用。"
                : "仍有系统级或更高权限的程序占用文件。";
        });
    }

    private void UpdateLockingProcesses(IReadOnlyList<LockingProcessInfo> processes)
    {
        _lockingProcesses.Clear();
        foreach (var process in processes)
        {
            _lockingProcesses.Add(process);
        }
    }
}
