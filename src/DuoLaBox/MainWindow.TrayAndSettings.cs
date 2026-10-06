using System.Windows;
using DuoLaBox.Models;

namespace DuoLaBox;

public partial class MainWindow
{
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(SettingsPage, SettingsNavButton);
    }

    private void WindowToolsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(WindowToolsPage, WindowToolsNavButton);
    }

    private void DayNightNavButton_Click(object sender, RoutedEventArgs e)
    {
        SystemToolsPage.SetSystemDarkMode(
            _systemThemeModeService.IsDarkMode);
        if (!_hostsFileLoaded && !SystemToolsPage.IsHostsBusy &&
            !SystemToolsPage.HasUnsavedHostsChanges)
        {
            LoadHostsFile();
        }
        ShowPage(SystemToolsPage, DayNightNavButton);
    }

    private void ColorPickerNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowPage(ColorPickerPage, ColorPickerNavButton);
    }

    private void FileBatchNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(FileBatchPage, FileBatchNavButton);
    }

    private void DiskCleanupNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowPage(DiskCleanupPage, DiskCleanupNavButton);
    }

    private async void SystemInformationNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowPage(SystemInformationPage, SystemInformationNavButton);
        if (!_systemInformationLoaded)
        {
            await RefreshSystemInformationAsync();
        }
    }

    private void SettingsPage_CloseToTrayChanged(bool closeToTray)
    {
        _settings.CloseToTrayOnClose = closeToTray;
        SaveSettings();
    }

    private async void SettingsPage_StartWithWindowsChanged(bool enabled) =>
        await UpdateStartupSettingsAsync(enabled, _settings.SilentStartup);

    private async void SettingsPage_SilentStartupChanged(bool enabled) =>
        await UpdateStartupSettingsAsync(_settings.StartWithWindows, enabled);

    private async Task UpdateStartupSettingsAsync(bool enabled, bool silent)
    {
        if (_exitRequested || SettingsPage.IsStartupBusy)
        {
            return;
        }
        SettingsPage.SetStartupBusy(true);
        try
        {
            var success = true;
            if (enabled || enabled != _settings.StartWithWindows)
            {
                success = await UpdateExternalIntegrationAsync(
                    () => _settingsService.ConfigureStartup(enabled, silent));
            }
            if (_exitRequested)
            {
                return;
            }
            if (success)
            {
                _settings.StartWithWindows = enabled;
                _settings.SilentStartup = silent;
                SaveSettings();
            }
            else
            {
                System.Windows.MessageBox.Show(
                    this,
                    "无法更新启动设置，请检查当前用户的注册表权限。",
                    AppIdentity.Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            SettingsPage.SetStartWithWindows(_settings.StartWithWindows);
            SettingsPage.SetSilentStartup(_settings.SilentStartup);
        }
        finally
        {
            SettingsPage.SetStartupBusy(false);
        }
    }

    private async Task<bool> UpdateExternalIntegrationAsync(Func<bool> action)
    {
        await _externalIntegrationGate.WaitAsync();
        try
        {
            return !_exitRequested && await Task.Run(action);
        }
        finally
        {
            _externalIntegrationGate.Release();
        }
    }

    private void SettingsPage_ThemeChanged(AppTheme theme)
    {
        _settings.Theme = theme;
        _themeService.SetTheme(theme);
        SaveSettings();
    }

    private async void FileBatchPage_ContextMenuSettingChanged(
        FileToolMode mode,
        bool enabled)
    {
        if (_exitRequested || FileBatchPage.AreContextMenusBusy)
        {
            return;
        }
        FileBatchPage.SetContextMenusBusy(true);
        try
        {
            var configured = await UpdateExternalIntegrationAsync(
                () => _shellContextMenuService.Configure(mode, enabled));
            if (_exitRequested)
            {
                return;
            }
            if (!configured)
            {
                FileBatchPage.SetContextMenuSetting(mode, GetContextMenuSetting(mode));
                System.Windows.MessageBox.Show(
                    this,
                    "无法更新资源管理器右键菜单，请检查当前用户的注册表权限。",
                    AppIdentity.Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SetContextMenuSetting(mode, enabled);
            SaveSettings();
        }
        finally
        {
            FileBatchPage.SetContextMenusBusy(false);
        }
    }

    private void TrayIcon_ShowRequested()
    {
        ShowFromTray();
    }

    private void TrayIcon_MousePickRequested()
    {
        BeginMousePicking();
    }

    private void TrayIcon_ExitRequested()
    {
        _exitRequested = true;
        Close();
    }

    private void HideToTray()
    {
        _refreshTimer.Stop();
        Hide();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (WindowToolsPage.Visibility == Visibility.Visible)
        {
            RefreshWindows(force: true);
            _refreshTimer.Start();
        }
    }

    private void LoadSettingsIntoPage()
    {
        SettingsPage.LoadSettings(_settings);
        FileBatchPage.LoadContextMenuSettings(_settings);
        SystemToolsPage.LoadSettings(
            _settings,
            _systemThemeModeService.IsDarkMode);
        ColorPickerPage.LoadFavorites(_settings.FavoriteColors);
        DiskCleanupPage.LoadLibrary(
            _cleanupLibraryService.Load(),
            $"当前清理库：{_cleanupLibraryService.CustomLibraryPath}");
        SystemToolsPage.SetKeepAwake(
            _keepAwakeService.IsEnabled,
            _keepAwakeService.KeepDisplayOn);
    }

    private bool GetContextMenuSetting(FileToolMode mode) =>
        mode switch
        {
            FileToolMode.Rename =>
                _settings.RenameContextMenuEnabled,
            FileToolMode.Unlock =>
                _settings.UnlockContextMenuEnabled,
            FileToolMode.ResizeImages =>
                _settings.ResizeContextMenuEnabled,
            _ => false
        };

    private void SetContextMenuSetting(
        FileToolMode mode,
        bool enabled)
    {
        switch (mode)
        {
            case FileToolMode.Rename:
                _settings.RenameContextMenuEnabled = enabled;
                break;
            case FileToolMode.Unlock:
                _settings.UnlockContextMenuEnabled = enabled;
                break;
            case FileToolMode.ResizeImages:
                _settings.ResizeContextMenuEnabled = enabled;
                break;
        }
    }

    private void SaveSettings()
    {
        if (_settingsService.Save(_settings))
        {
            return;
        }

        System.Windows.MessageBox.Show(
            this,
            "设置无法保存到本地配置文件。",
            AppIdentity.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowPage(
        UIElement page,
        System.Windows.Controls.Button selectedNavigation)
    {
        _refreshTimer.Stop();

        UIElement[] pages =
        [
            WindowToolsPage,
            SystemToolsPage,
            FileBatchPage,
            ColorPickerPage,
            DiskCleanupPage,
            SystemInformationPage,
            SettingsPage
        ];
        foreach (var candidate in pages)
        {
            candidate.Visibility = Visibility.Collapsed;
        }

        System.Windows.Controls.Button[] navigationButtons =
        [
            WindowToolsNavButton,
            DayNightNavButton,
            FileBatchNavButton,
            ColorPickerNavButton,
            DiskCleanupNavButton,
            SystemInformationNavButton,
            SettingsNavButton
        ];
        foreach (var navigationButton in navigationButtons)
        {
            navigationButton.Background =
                System.Windows.Media.Brushes.Transparent;
            navigationButton.Tag = null;
        }

        page.Visibility = Visibility.Visible;
        selectedNavigation.SetResourceReference(
            System.Windows.Controls.Control.BackgroundProperty,
            "NavigationSelectedBrush");
        selectedNavigation.Tag = "Selected";

        if (ReferenceEquals(page, WindowToolsPage))
        {
            RefreshWindows(force: true);
            _refreshTimer.Start();
        }
    }

    public void HandleStartupArguments(string[] arguments)
    {
        if (_exitRequested)
        {
            return;
        }
        if (arguments.Length == 0 ||
            arguments.Any(argument =>
                string.Equals(
                    argument,
                    "--show",
                    StringComparison.OrdinalIgnoreCase)))
        {
            ShowFromTray();
            return;
        }

        if (arguments.Any(argument =>
                string.Equals(
                    argument,
                    "--silent",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var optionIndex = Array.FindIndex(
            arguments,
            argument => string.Equals(
                argument,
                "--file-tool",
                StringComparison.OrdinalIgnoreCase));
        if (optionIndex < 0 ||
            optionIndex + 1 >= arguments.Length)
        {
            return;
        }

        var mode = arguments[optionIndex + 1].ToLowerInvariant() switch
        {
            "rename" => FileToolMode.Rename,
            "unlock" => FileToolMode.Unlock,
            "resize" => FileToolMode.ResizeImages,
            _ => (FileToolMode?)null
        };
        if (mode is null)
        {
            return;
        }

        var paths = arguments
            .Skip(optionIndex + 2)
            .Where(System.IO.File.Exists)
            .ToArray();
        ShowFromTray();
        ShowPage(FileBatchPage, FileBatchNavButton);
        FileBatchPage.Open(mode.Value, paths);
    }
}
