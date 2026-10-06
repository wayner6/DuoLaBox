using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DuoLaBox.Models;
using Microsoft.Win32;

namespace DuoLaBox.Services;

public sealed class ThemeService : IDisposable
{
    private const string PersonalizeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private AppTheme _theme;
    private bool _isDarkApplied;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    public void SetTheme(AppTheme theme)
    {
        _theme = theme;
        ApplyTheme(theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDarkTheme()
        });
    }

    public void PreviewSystemTheme(bool dark)
    {
        if (_theme == AppTheme.FollowSystem)
        {
            ApplyTheme(dark);
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                PersonalizeRegistryPath);
            return key?.GetValue("AppsUseLightTheme") is int value &&
                   value == 0;
        }
        catch
        {
            return false;
        }
    }

    public void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var darkMode = _isDarkApplied ? 1 : 0;
        if (NativeMethods.DwmSetWindowAttribute(
                handle,
                NativeMethods.DwmwaUseImmersiveDarkMode,
                ref darkMode,
                sizeof(int)) != 0)
        {
            NativeMethods.DwmSetWindowAttribute(
                handle,
                NativeMethods.DwmwaUseImmersiveDarkModeBefore20H1,
                ref darkMode,
                sizeof(int));
        }

        var captionColor = _isDarkApplied
            ? 0x00202020
            : 0x00FAF7F3;
        var textColor = _isDarkApplied
            ? 0x00FFFFFF
            : 0x001B1B1B;
        NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmwaCaptionColor,
            ref captionColor,
            sizeof(int));
        NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmwaTextColor,
            ref textColor,
            sizeof(int));
        var borderColor = NativeMethods.DwmwaColorNone;
        NativeMethods.DwmSetWindowAttribute(
            handle,
            NativeMethods.DwmwaBorderColor,
            ref borderColor,
            sizeof(int));

    }

    private void ApplyTheme(bool dark)
    {
        _isDarkApplied = dark;
        var resources = System.Windows.Application.Current.Resources;
        SetBrush(
            resources,
            "WindowBackgroundBrush",
            dark ? "#202020" : "#F3F7FA");
        SetBrush(
            resources,
            "SurfaceBrush",
            dark ? "#2B2B2B" : "#FCFDFE");
        SetBrush(
            resources,
            "BorderBrush",
            dark ? "#3D3D3D" : "#DCE4E8");
        SetBrush(resources, "PrimaryTextBrush", dark ? "#FFFFFF" : "#1B1B1B");
        SetBrush(resources, "SecondaryTextBrush", dark ? "#C8C8C8" : "#606060");
        SetBrush(
            resources,
            "SidebarBrush",
            dark ? "#191919" : "#EDF4F7");
        SetBrush(
            resources,
            "NavigationSelectedBrush",
            dark ? "#CC323232" : "#CCDEE8ED");
        SetBrush(
            resources,
            "ControlHoverBrush",
            dark ? "#CC383838" : "#CCE9F0F3");
        SetBrush(
            resources,
            "ControlPressedBrush",
            dark ? "#CC414141" : "#CCDDE7EB");
        SetBrush(resources, "ControlBorderBrush", dark ? "#515151" : "#C9D5DB");
        SetBrush(resources, "SearchBorderBrush", dark ? "#656565" : "#AEBCC3");
        SetBrush(resources, "TertiaryTextBrush", dark ? "#9D9D9D" : "#888888");
        SetBrush(resources, "AccentSubtleBrush", dark ? "#173A56" : "#E8F2FA");
        foreach (Window window in
                 System.Windows.Application.Current.Windows)
        {
            ApplyWindowChrome(window);
        }
    }

    private static void SetBrush(
        ResourceDictionary resources,
        string key,
        string color)
    {
        resources[key] = new SolidColorBrush(
            (System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void SystemEvents_UserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (_theme != AppTheme.FollowSystem)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            () => ApplyTheme(IsSystemDarkTheme()));
    }
}
