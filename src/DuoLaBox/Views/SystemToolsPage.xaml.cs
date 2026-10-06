using System.Globalization;
using System.Windows;
using DuoLaBox.Models;

namespace DuoLaBox.Views;

public partial class SystemToolsPage :
    System.Windows.Controls.UserControl
{
    private bool _loading;
    private TimeSpan _darkStart;
    private TimeSpan _lightStart;
    private string _savedHostsContent = string.Empty;

    public bool HasUnsavedHostsChanges => HostsTextBox.Text != _savedHostsContent;
    public bool IsHostsBusy { get; private set; }
    public bool IsThemeBusy { get; private set; }

    public void SetHostsBusy(bool busy)
    {
        IsHostsBusy = busy;
        HostsReloadButton.IsEnabled = !busy;
        HostsSaveButton.IsEnabled = !busy;
    }

    public void SetThemeBusy(bool busy)
    {
        IsThemeBusy = busy;
        NightModeToggle.IsEnabled = !busy;
        ScheduleToggle.IsEnabled = !busy;
        DarkStartTextBox.IsEnabled = !busy;
        LightStartTextBox.IsEnabled = !busy;
    }

    public SystemToolsPage()
    {
        InitializeComponent();
    }

    public event Action<bool>? ManualModeChanged;

    public event Action<bool, TimeSpan, TimeSpan>? ScheduleChanged;

    public event Action? HostsReloadRequested;

    public event Action<string>? HostsSaveRequested;

    public event Action<bool, bool>? KeepAwakeChanged;

    public void LoadSettings(AppSettings settings, bool isDarkMode)
    {
        _loading = true;
        SetSystemDarkMode(isDarkMode);
        ScheduleToggle.IsChecked = settings.DayNightScheduleEnabled;
        ScheduleToggle.Content = settings.DayNightScheduleEnabled
            ? "开"
            : "关";
        _darkStart = settings.DarkModeStartTime;
        _lightStart = settings.LightModeStartTime;
        DarkStartTextBox.Text = FormatTime(_darkStart);
        LightStartTextBox.Text = FormatTime(_lightStart);
        _loading = false;
    }

    public void SetSystemDarkMode(bool dark)
    {
        var wasLoading = _loading;
        _loading = true;
        NightModeToggle.IsChecked = dark;
        NightModeToggle.Content = dark ? "开" : "关";
        CurrentModeText.Text = dark
            ? "当前 Windows 使用夜间（深色）模式"
            : "当前 Windows 使用日间（浅色）模式";
        _loading = wasLoading;
    }

    public void SetHostsContent(string content, string status)
    {
        HostsTextBox.Text = content;
        MarkHostsSaved(content, status);
    }

    public void MarkHostsSaved(string content, string status)
    {
        _savedHostsContent = content;
        HostsStatusText.Text = status;
    }

    public void SetHostsStatus(string status)
    {
        HostsStatusText.Text = status;
    }

    public void SetKeepAwake(
        bool enabled,
        bool keepDisplayOn,
        string? status = null)
    {
        var wasLoading = _loading;
        _loading = true;
        KeepAwakeToggle.IsChecked = enabled;
        KeepAwakeToggle.Content = enabled ? "开" : "关";
        KeepDisplayToggle.IsEnabled = enabled;
        KeepDisplayToggle.IsChecked = enabled && keepDisplayOn;
        KeepDisplayToggle.Content =
            enabled && keepDisplayOn ? "开" : "关";
        KeepAwakeStatusText.Text = status ??
            (enabled
                ? keepDisplayOn
                    ? "正在保持电脑和屏幕唤醒。"
                    : "正在保持电脑唤醒，屏幕可正常熄灭。"
                : "当前未保持唤醒。");
        _loading = wasLoading;
    }

    private void NightModeToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading || IsThemeBusy)
        {
            return;
        }

        var dark = NightModeToggle.IsChecked == true;
        SetSystemDarkMode(dark);
        ManualModeChanged?.Invoke(dark);
    }

    private void ScheduleToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = ScheduleToggle.IsChecked == true;
        ScheduleToggle.Content = enabled ? "开" : "关";
        RaiseScheduleChanged();
    }

    private void ScheduleTime_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (TryReadTimes(out var darkStart, out var lightStart))
        {
            _darkStart = darkStart;
            _lightStart = lightStart;
            NormalizeTimeText();
            RaiseScheduleChanged();
            return;
        }

        NormalizeTimeText();
    }

    private void ScheduleTime_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }

        System.Windows.Input.Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void RaiseScheduleChanged()
    {
        ScheduleChanged?.Invoke(
            ScheduleToggle.IsChecked == true,
            _darkStart,
            _lightStart);
    }

    private bool TryReadTimes(
        out TimeSpan darkStart,
        out TimeSpan lightStart)
    {
        var darkValid = TryReadTime(
            DarkStartTextBox.Text,
            out darkStart);
        var lightValid = TryReadTime(
            LightStartTextBox.Text,
            out lightStart);
        return darkValid && lightValid;
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.ToString(
            @"hh\:mm",
            CultureInfo.InvariantCulture);
    }

    private static bool TryReadTime(
        string text,
        out TimeSpan time)
    {
        if (TimeSpan.TryParse(
                text,
                CultureInfo.CurrentCulture,
                out time) &&
            time >= TimeSpan.Zero &&
            time < TimeSpan.FromDays(1))
        {
            return true;
        }

        time = default;
        return false;
    }

    private void NormalizeTimeText()
    {
        DarkStartTextBox.Text = FormatTime(_darkStart);
        LightStartTextBox.Text = FormatTime(_lightStart);
    }

    private void ReloadHostsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsHostsBusy)
        {
            HostsReloadRequested?.Invoke();
        }
    }

    private void SaveHostsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsHostsBusy)
        {
            HostsSaveRequested?.Invoke(HostsTextBox.Text);
        }
    }

    private void KeepAwakeToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = KeepAwakeToggle.IsChecked == true;
        SetKeepAwake(
            enabled,
            enabled && KeepDisplayToggle.IsChecked == true);
        KeepAwakeChanged?.Invoke(
            enabled,
            enabled && KeepDisplayToggle.IsChecked == true);
    }

    private void KeepDisplayToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading || KeepAwakeToggle.IsChecked != true)
        {
            return;
        }

        var keepDisplayOn = KeepDisplayToggle.IsChecked == true;
        SetKeepAwake(enabled: true, keepDisplayOn);
        KeepAwakeChanged?.Invoke(true, keepDisplayOn);
    }

}

