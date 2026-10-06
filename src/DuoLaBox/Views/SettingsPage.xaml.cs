using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using DuoLaBox.Models;
using WpfControls = System.Windows.Controls;

namespace DuoLaBox.Views;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private bool _loading;

    public bool IsStartupBusy { get; private set; }

    public void SetStartupBusy(bool busy)
    {
        IsStartupBusy = busy;
        StartWithWindowsToggle.IsEnabled = !busy;
        SilentStartupToggle.IsEnabled = !busy;
    }

    public SettingsPage()
    {
        InitializeComponent();
        VersionText.Text =
            $"版本 {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知"} · 作者 Wayne";
    }

    public event Action<bool>? CloseToTrayChanged;

    public event Action<bool>? StartWithWindowsChanged;

    public event Action<bool>? SilentStartupChanged;

    public event Action<AppTheme>? ThemeChanged;

    public void LoadSettings(AppSettings settings)
    {
        _loading = true;
        CloseBehaviorComboBox.SelectedIndex =
            settings.CloseToTrayOnClose ? 0 : 1;
        SetStartWithWindows(settings.StartWithWindows);
        SetSilentStartup(settings.SilentStartup);
        ThemeComboBox.SelectedIndex = settings.Theme switch
        {
            AppTheme.Light => 0,
            AppTheme.Dark => 1,
            _ => 2
        };
        _loading = false;
    }

    public void SetStartWithWindows(bool enabled)
    {
        StartWithWindowsToggle.IsChecked = enabled;
        StartWithWindowsToggle.Content = enabled
            ? "开"
            : "关";
    }

    public void SetSilentStartup(bool enabled)
    {
        SilentStartupToggle.IsChecked = enabled;
        SilentStartupToggle.Content = enabled ? "开" : "关";
    }

    private void CloseBehaviorComboBox_SelectionChanged(
        object sender,
        WpfControls.SelectionChangedEventArgs e)
    {
        if (_loading ||
            CloseBehaviorComboBox.SelectedItem is not
                WpfControls.ComboBoxItem { Tag: string behavior })
        {
            return;
        }

        CloseToTrayChanged?.Invoke(behavior == "Tray");
    }

    private void StartWithWindowsToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading || IsStartupBusy)
        {
            return;
        }

        var enabled = StartWithWindowsToggle.IsChecked == true;
        SetStartWithWindows(enabled);
        StartWithWindowsChanged?.Invoke(enabled);
    }

    private void SilentStartupToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_loading || IsStartupBusy)
        {
            return;
        }

        var enabled = SilentStartupToggle.IsChecked == true;
        SetSilentStartup(enabled);
        SilentStartupChanged?.Invoke(enabled);
    }

    private void ThemeComboBox_SelectionChanged(
        object sender,
        WpfControls.SelectionChangedEventArgs e)
    {
        if (_loading ||
            ThemeComboBox.SelectedItem is not
                WpfControls.ComboBoxItem { Tag: string themeName } ||
            !Enum.TryParse<AppTheme>(themeName, out var theme))
        {
            return;
        }

        ThemeChanged?.Invoke(theme);
    }

    private void GithubButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://github.com/wayner6/DuoLaBox")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 浏览器不可用时不影响设置页面。
        }
    }
}
