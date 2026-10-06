using System.Windows.Threading;
using DuoLaBox.Models;

namespace DuoLaBox.Services;

public sealed class DayNightScheduleService : IDisposable
{
    private readonly SystemThemeModeService _themeModeService;
    private readonly DispatcherTimer _timer;

    private bool _enabled;
    private TimeSpan _darkModeStart;
    private TimeSpan _lightModeStart;
    private int _isApplying;

    public DayNightScheduleService(
        SystemThemeModeService themeModeService)
    {
        _themeModeService = themeModeService;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += Timer_Tick;
    }

    public void Update(AppSettings settings)
    {
        _enabled = settings.DayNightScheduleEnabled;
        _darkModeStart = settings.DarkModeStartTime;
        _lightModeStart = settings.LightModeStartTime;

        if (!_enabled)
        {
            _timer.Stop();
            return;
        }

        _ = ApplyScheduledModeAsync();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        await ApplyScheduledModeAsync();
    }

    private async Task ApplyScheduledModeAsync()
    {
        if (Interlocked.Exchange(ref _isApplying, 1) != 0)
        {
            return;
        }

        try
        {
            var shouldBeDark = IsWithinDarkPeriod(
                DateTime.Now.TimeOfDay,
                _darkModeStart,
                _lightModeStart);
            if (_themeModeService.IsDarkMode != shouldBeDark)
            {
                await _themeModeService.SetDarkModeAsync(
                    shouldBeDark);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isApplying, 0);
        }
    }

    private static bool IsWithinDarkPeriod(
        TimeSpan current,
        TimeSpan darkStart,
        TimeSpan lightStart)
    {
        if (darkStart == lightStart)
        {
            return false;
        }

        return darkStart > lightStart
            ? current >= darkStart || current < lightStart
            : current >= darkStart && current < lightStart;
    }
}
