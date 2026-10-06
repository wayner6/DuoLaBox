using Microsoft.Win32;

namespace DuoLaBox.Services;

public sealed class SystemThemeModeService : IDisposable
{
    private const string PersonalizeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private readonly SemaphoreSlim _modeGate = new(1, 1);
    private volatile bool _disposed;

    public SystemThemeModeService()
    {
        _dispatcher =
            System.Windows.Threading.Dispatcher.CurrentDispatcher;
        SystemEvents.UserPreferenceChanged +=
            SystemEvents_UserPreferenceChanged;
    }

    public event Action<bool>? ModeChanged;

    public bool IsDarkMode
    {
        get
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
    }

    public async Task<bool> SetDarkModeAsync(bool dark)
    {
        await _modeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return !_disposed && await Task.Run(() => SetDarkMode(dark)).ConfigureAwait(false);
        }
        finally
        {
            _modeGate.Release();
        }
    }

    private bool SetDarkMode(bool dark)
    {
        if (_disposed)
        {
            return false;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                PersonalizeRegistryPath,
                writable: true);
            if (key is null)
            {
                return false;
            }

            var lightValue = dark ? 0 : 1;
            key.SetValue(
                "AppsUseLightTheme",
                lightValue,
                RegistryValueKind.DWord);
            key.SetValue(
                "SystemUsesLightTheme",
                lightValue,
                RegistryValueKind.DWord);

            NativeMethods.SendMessageTimeout(
                NativeMethods.HwndBroadcast,
                NativeMethods.WmSettingChange,
                0,
                "ImmersiveColorSet",
                NativeMethods.SmtoAbortIfHung,
                1000,
                out _);
            NotifyModeChanged(dark);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        SystemEvents.UserPreferenceChanged -=
            SystemEvents_UserPreferenceChanged;
    }

    private void SystemEvents_UserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        NotifyModeChanged(IsDarkMode);
    }

    private void NotifyModeChanged(bool dark)
    {
        if (_disposed || _dispatcher.HasShutdownStarted)
        {
            return;
        }
        if (_dispatcher.CheckAccess())
        {
            ModeChanged?.Invoke(dark);
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ModeChanged?.Invoke(dark);
            }
        });
    }
}
