using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DuoLaBox.Models;

namespace DuoLaBox.Views;

public partial class SystemInformationPage :
    System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _uptimeTimer;
    private DateTimeOffset? _bootTime;

    public SystemInformationPage()
    {
        InitializeComponent();
        _uptimeTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => UpdateUptime(),
            Dispatcher);
        _uptimeTimer.Stop();
    }

    public event Action? RefreshRequested;

    public void SetScanning()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "正在扫描系统和硬件信息…";
    }

    public void SetSnapshot(SystemInformationSnapshot snapshot)
    {
        ModelText.Text = snapshot.ModelSummary;
        OperatingSystemText.Text = snapshot.OperatingSystemSummary;
        DetailsList.ItemsSource = snapshot.Groups;
        _bootTime = snapshot.BootTime;
        UpdateUptime();
        RefreshButton.IsEnabled = true;
        StatusText.Text = $"扫描完成 · {DateTime.Now:HH:mm:ss}";
    }

    public void SetError(string message)
    {
        RefreshButton.IsEnabled = true;
        StatusText.Text = $"扫描失败：{message}";
    }

    private void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshRequested?.Invoke();
    }

    private void SystemInformationPage_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _uptimeTimer.Start();
            UpdateUptime();
            return;
        }

        _uptimeTimer.Stop();
    }

    private void UpdateUptime()
    {
        if (_bootTime is null)
        {
            return;
        }

        var uptime = DateTimeOffset.Now - _bootTime.Value;
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        UptimeText.Text =
            $"{(int)uptime.TotalDays} 天 " +
            $"{uptime.Hours} 小时 {uptime.Minutes} 分钟 " +
            $"{uptime.Seconds} 秒";
    }
}
