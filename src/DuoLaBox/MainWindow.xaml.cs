using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using DuoLaBox.Models;
using DuoLaBox.Services;
using DuoLaBox.Views;

namespace DuoLaBox;

public partial class MainWindow : Window
{
    private readonly TopMostCoordinator _topMostCoordinator = new();
    private readonly WindowPickerService _windowPicker = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly ThemeService _themeService;
    private readonly TrayIconService _trayIcon;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SystemThemeModeService _systemThemeModeService;
    private readonly DayNightScheduleService _dayNightScheduleService;
    private readonly ShellContextMenuService _shellContextMenuService = new();
    private readonly ScreenColorPickerService _screenColorPicker = new();
    private readonly HostsFileService _hostsFileService = new();
    private readonly KeepAwakeService _keepAwakeService = new();
    private readonly ColorPreviewWindow _colorPreviewWindow;
    private readonly CleanupLibraryService _cleanupLibraryService = new();
    private readonly DiskCleanupService _diskCleanupService = new();
    private readonly SystemInformationService
        _systemInformationService = new();
    private CancellationTokenSource? _cleanupScanCancellation;
    private CancellationTokenSource? _systemInformationCancellation;

    private AppSettings _settings;
    private ICollectionView? _windowView;
    private string _windowSnapshot = string.Empty;
    private bool _exitRequested;
    private bool _startupInitialized;
    private readonly SemaphoreSlim _externalIntegrationGate = new(1, 1);

    public bool SilentStartupEnabled => _settings.SilentStartup;

    public MainWindow()
    {
        _settings = _settingsService.Load();
        _themeService = new ThemeService();
        _themeService.SetTheme(_settings.Theme);
        _systemThemeModeService = new SystemThemeModeService();
        _dayNightScheduleService = new DayNightScheduleService(
            _systemThemeModeService);

        InitializeComponent();
        VersionText.Text =
            $"版本 {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        _colorPreviewWindow = new ColorPreviewWindow();

        _trayIcon = new TrayIconService();
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _refreshTimer.Tick += RefreshTimer_Tick;
        _windowPicker.WindowPicked += WindowPicker_WindowPicked;
        _windowPicker.InvalidWindowTopClick += WindowPicker_InvalidWindowTopClick;
        _topMostCoordinator.MarkerUnpinned += TopMostCoordinator_MarkerUnpinned;
        _topMostCoordinator.MarkerUnpinFailed += TopMostCoordinator_MarkerUnpinFailed;
        _trayIcon.ShowRequested += TrayIcon_ShowRequested;
        _trayIcon.MousePickRequested += TrayIcon_MousePickRequested;
        _trayIcon.ExitRequested += TrayIcon_ExitRequested;
        SettingsPage.CloseToTrayChanged +=
            SettingsPage_CloseToTrayChanged;
        SettingsPage.StartWithWindowsChanged +=
            SettingsPage_StartWithWindowsChanged;
        SettingsPage.SilentStartupChanged +=
            SettingsPage_SilentStartupChanged;
        SettingsPage.ThemeChanged += SettingsPage_ThemeChanged;
        FileBatchPage.ContextMenuSettingChanged +=
            FileBatchPage_ContextMenuSettingChanged;
        SystemToolsPage.ManualModeChanged +=
            SystemToolsPage_ManualModeChanged;
        SystemToolsPage.ScheduleChanged +=
            SystemToolsPage_ScheduleChanged;
        SystemToolsPage.HostsReloadRequested +=
            SystemToolsPage_HostsReloadRequested;
        SystemToolsPage.HostsSaveRequested +=
            SystemToolsPage_HostsSaveRequested;
        SystemToolsPage.KeepAwakeChanged +=
            SystemToolsPage_KeepAwakeChanged;
        ColorPickerPage.PickingChanged +=
            ColorPickerPage_PickingChanged;
        ColorPickerPage.FavoritesChanged +=
            ColorPickerPage_FavoritesChanged;
        DiskCleanupPage.ScanRequested +=
            DiskCleanupPage_ScanRequested;
        DiskCleanupPage.CleanRequested +=
            DiskCleanupPage_CleanRequested;
        DiskCleanupPage.SaveLibraryRequested +=
            DiskCleanupPage_SaveLibraryRequested;
        DiskCleanupPage.RestoreLibraryRequested +=
            DiskCleanupPage_RestoreLibraryRequested;
        DiskCleanupPage.ImportLibraryRequested +=
            DiskCleanupPage_ImportLibraryRequested;
        SystemInformationPage.RefreshRequested +=
            SystemInformationPage_RefreshRequested;
        _screenColorPicker.ColorPicked +=
            ScreenColorPicker_ColorPicked;
        _screenColorPicker.PickingFailed +=
            ScreenColorPicker_PickingFailed;
        _screenColorPicker.PreviewChanged +=
            ScreenColorPicker_PreviewChanged;
        _systemThemeModeService.ModeChanged +=
            SystemThemeModeService_ModeChanged;

    }

    public void InitializeStartup()
    {
        if (_startupInitialized)
        {
            return;
        }

        LoadSettingsIntoPage();
        _dayNightScheduleService.Update(_settings);
        _startupInitialized = true;
        var settings = _settings.Clone();
        _ = UpdateExternalIntegrationAsync(() =>
        {
            SynchronizeExternalIntegrations(settings);
            return true;
        });
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeStartup();
        if (WindowToolsPage.Visibility == Visibility.Visible)
        {
            RefreshWindows(force: true);
            _refreshTimer.Start();
        }
    }

    private void SynchronizeExternalIntegrations(AppSettings settings)
    {
        if (settings.StartWithWindows)
        {
            _settingsService.ConfigureStartup(
                enabled: true,
                settings.SilentStartup);
        }

        _shellContextMenuService.Configure(
            FileToolMode.Rename,
            settings.RenameContextMenuEnabled);
        _shellContextMenuService.Configure(
            FileToolMode.Unlock,
            settings.UnlockContextMenuEnabled);
        _shellContextMenuService.Configure(
            FileToolMode.ResizeImages,
            settings.ResizeContextMenuEnabled);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _themeService.ApplyWindowChrome(this);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _refreshTimer.Stop();
            return;
        }

        if (IsVisible &&
            WindowToolsPage.Visibility == Visibility.Visible)
        {
            RefreshWindows();
            _refreshTimer.Start();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested && _settings.CloseToTrayOnClose)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (FileBatchPage.IsBusy || SystemToolsPage.IsHostsBusy ||
            SystemToolsPage.IsThemeBusy || SettingsPage.IsStartupBusy ||
            FileBatchPage.AreContextMenusBusy)
        {
            e.Cancel = true;
            _exitRequested = false;
            System.Windows.MessageBox.Show(
                this,
                "当前任务尚未结束。请等待完成，或取消正在等待确认的操作后再退出。",
                AppIdentity.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _exitRequested = true;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;

        _windowPicker.WindowPicked -= WindowPicker_WindowPicked;
        _windowPicker.InvalidWindowTopClick -= WindowPicker_InvalidWindowTopClick;
        _topMostCoordinator.MarkerUnpinned -= TopMostCoordinator_MarkerUnpinned;
        _topMostCoordinator.MarkerUnpinFailed -= TopMostCoordinator_MarkerUnpinFailed;
        _trayIcon.ShowRequested -= TrayIcon_ShowRequested;
        _trayIcon.MousePickRequested -= TrayIcon_MousePickRequested;
        _trayIcon.ExitRequested -= TrayIcon_ExitRequested;
        SettingsPage.CloseToTrayChanged -=
            SettingsPage_CloseToTrayChanged;
        SettingsPage.StartWithWindowsChanged -=
            SettingsPage_StartWithWindowsChanged;
        SettingsPage.SilentStartupChanged -=
            SettingsPage_SilentStartupChanged;
        SettingsPage.ThemeChanged -= SettingsPage_ThemeChanged;
        FileBatchPage.ContextMenuSettingChanged -=
            FileBatchPage_ContextMenuSettingChanged;
        SystemToolsPage.ManualModeChanged -=
            SystemToolsPage_ManualModeChanged;
        SystemToolsPage.ScheduleChanged -=
            SystemToolsPage_ScheduleChanged;
        SystemToolsPage.HostsReloadRequested -=
            SystemToolsPage_HostsReloadRequested;
        SystemToolsPage.HostsSaveRequested -=
            SystemToolsPage_HostsSaveRequested;
        SystemToolsPage.KeepAwakeChanged -=
            SystemToolsPage_KeepAwakeChanged;
        ColorPickerPage.PickingChanged -=
            ColorPickerPage_PickingChanged;
        ColorPickerPage.FavoritesChanged -=
            ColorPickerPage_FavoritesChanged;
        DiskCleanupPage.ScanRequested -=
            DiskCleanupPage_ScanRequested;
        DiskCleanupPage.CleanRequested -=
            DiskCleanupPage_CleanRequested;
        DiskCleanupPage.SaveLibraryRequested -=
            DiskCleanupPage_SaveLibraryRequested;
        DiskCleanupPage.RestoreLibraryRequested -=
            DiskCleanupPage_RestoreLibraryRequested;
        DiskCleanupPage.ImportLibraryRequested -=
            DiskCleanupPage_ImportLibraryRequested;
        SystemInformationPage.RefreshRequested -=
            SystemInformationPage_RefreshRequested;
        _screenColorPicker.ColorPicked -=
            ScreenColorPicker_ColorPicked;
        _screenColorPicker.PickingFailed -=
            ScreenColorPicker_PickingFailed;
        _screenColorPicker.PreviewChanged -=
            ScreenColorPicker_PreviewChanged;
        _systemThemeModeService.ModeChanged -=
            SystemThemeModeService_ModeChanged;

        _windowPicker.Dispose();
        _cleanupScanCancellation?.Cancel();
        _cleanupScanCancellation?.Dispose();
        _systemInformationCancellation?.Cancel();
        _systemInformationCancellation?.Dispose();
        _screenColorPicker.Dispose();
        _colorPreviewWindow.Close();
        _keepAwakeService.Dispose();
        _topMostCoordinator.Dispose();
        _trayIcon.Dispose();
        _dayNightScheduleService.Dispose();
        _systemThemeModeService.Dispose();
        _themeService.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}

