using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using DuoLaBox.Models;
using DuoLaBox.Services;

namespace DuoLaBox;

public partial class MainWindow
{
    private bool _hostsFileLoaded;

    private async void SystemToolsPage_ManualModeChanged(bool dark)
    {
        if (_exitRequested || SystemToolsPage.IsThemeBusy)
        {
            return;
        }
        SystemToolsPage.SetThemeBusy(true);
        try
        {
            _themeService.PreviewSystemTheme(dark);
            var success = await _systemThemeModeService.SetDarkModeAsync(dark);
            if (_exitRequested)
            {
                return;
            }
            var actualMode = _systemThemeModeService.IsDarkMode;
            SystemToolsPage.SetSystemDarkMode(actualMode);
            _themeService.PreviewSystemTheme(actualMode);
            if (!success)
            {
                System.Windows.MessageBox.Show(
                    this,
                    "无法切换 Windows 系统颜色模式。",
                    AppIdentity.Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            SystemToolsPage.SetThemeBusy(false);
        }
    }

    private void SystemToolsPage_ScheduleChanged(
        bool enabled,
        TimeSpan darkStart,
        TimeSpan lightStart)
    {
        _settings.DayNightScheduleEnabled = enabled;
        _settings.DarkModeStartTime = darkStart;
        _settings.LightModeStartTime = lightStart;
        SaveSettings();
        _dayNightScheduleService.Update(_settings);
    }

    private void SystemThemeModeService_ModeChanged(bool dark)
    {
        SystemToolsPage.SetSystemDarkMode(dark);
        if (_settings.Theme == AppTheme.FollowSystem)
        {
            _themeService.SetTheme(AppTheme.FollowSystem);
        }
    }

    private void SystemToolsPage_HostsReloadRequested()
    {
        if (_exitRequested || SystemToolsPage.IsHostsBusy)
        {
            return;
        }
        if (SystemToolsPage.HasUnsavedHostsChanges &&
            System.Windows.MessageBox.Show(
                this,
                "重新读取将丢弃尚未保存的 Hosts 修改。是否继续？",
                AppIdentity.Name,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        LoadHostsFile();
    }

    private async void SystemToolsPage_HostsSaveRequested(string content)
    {
        if (_exitRequested || SystemToolsPage.IsHostsBusy)
        {
            return;
        }
        SystemToolsPage.SetHostsBusy(true);
        try
        {
            await SaveHostsFileAsync(content);
        }
        catch (Exception exception)
        {
            if (!_exitRequested)
            {
                SystemToolsPage.SetHostsStatus($"保存失败：{exception.Message}");
            }
        }
        finally
        {
            SystemToolsPage.SetHostsBusy(false);
        }
    }

    private async Task SaveHostsFileAsync(string content)
    {
        SystemToolsPage.SetHostsStatus("正在准备保存…");
        if (_hostsFileService.IsAdministrator)
        {
            var directResult = await Task.Run(
                () => _hostsFileService.WriteAsAdministrator(content));
            if (_exitRequested)
            {
                return;
            }
            if (directResult.Success)
            {
                _hostsFileLoaded = true;
                SystemToolsPage.MarkHostsSaved(content, directResult.Message);
            }
            else
            {
                SystemToolsPage.SetHostsStatus(directResult.Message);
            }
            return;
        }

        StagedFile stagedFile;
        try
        {
            stagedFile = _hostsFileService.StageContent(content);
        }
        catch (Exception exception)
        {
            SystemToolsPage.SetHostsStatus(
                $"无法创建临时文件：{exception.Message}");
            return;
        }

        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                SystemToolsPage.SetHostsStatus("无法确定当前程序路径。");
                return;
            }

            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            startInfo.ArgumentList.Add("--apply-hosts");
            startInfo.ArgumentList.Add(stagedFile.Path);
            startInfo.ArgumentList.Add(stagedFile.Sha256);
            using var helper = Process.Start(startInfo);
            if (helper is null)
            {
                SystemToolsPage.SetHostsStatus("未能启动管理员保存程序。");
                return;
            }

            SystemToolsPage.SetHostsStatus("请在系统窗口中确认管理员权限…");
            await helper.WaitForExitAsync();
            if (_exitRequested)
            {
                return;
            }
            if (helper.ExitCode == 0)
            {
                var refreshed = _hostsFileService.Read();
                _hostsFileLoaded = true;
                SystemToolsPage.MarkHostsSaved(
                    refreshed.Success ? refreshed.Content : content,
                    refreshed.Success
                        ? "保存成功，已在 Hosts 目录创建带时间戳的备份。"
                        : refreshed.Message);
            }
            else
            {
                SystemToolsPage.SetHostsStatus(
                    "Hosts 保存未完成，请检查管理员权限或文件占用。");
            }
        }
        catch (Win32Exception exception)
            when (exception.NativeErrorCode == 1223)
        {
            SystemToolsPage.SetHostsStatus("已取消管理员权限请求，未保存修改。");
        }
        catch (Exception exception)
        {
            SystemToolsPage.SetHostsStatus($"保存失败：{exception.Message}");
        }
        finally
        {
            _hostsFileService.CleanupStagingFile(stagedFile.Path);
        }
    }

    private void SystemToolsPage_KeepAwakeChanged(
        bool enabled,
        bool keepDisplayOn)
    {
        if (_keepAwakeService.SetEnabled(enabled, keepDisplayOn))
        {
            SystemToolsPage.SetKeepAwake(enabled, keepDisplayOn);
            return;
        }

        SystemToolsPage.SetKeepAwake(
            _keepAwakeService.IsEnabled,
            _keepAwakeService.KeepDisplayOn,
            "Windows 未接受保持唤醒请求。");
    }

    private void LoadHostsFile()
    {
        var result = _hostsFileService.Read();
        if (result.Success)
        {
            _hostsFileLoaded = true;
            SystemToolsPage.SetHostsContent(
                result.Content,
                $"{result.Message} 路径：{_hostsFileService.HostsPath}");
            return;
        }

        SystemToolsPage.SetHostsStatus(result.Message);
    }
}

