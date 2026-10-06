using System.Drawing;
using System.Windows.Threading;
using DuoLaBox.Views;
using Forms = System.Windows.Forms;

namespace DuoLaBox.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Dispatcher _dispatcher;
    private readonly Icon _appIcon;
    private readonly TrayMenuWindow _trayMenu;

    public TrayIconService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _trayMenu = new TrayMenuWindow();
        _trayMenu.ShowRequested += () => ShowRequested?.Invoke();
        _trayMenu.MousePickRequested += () => MousePickRequested?.Invoke();
        _trayMenu.ExitRequested += () => ExitRequested?.Invoke();

        _appIcon = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? Icon.ExtractAssociatedIcon(Environment.ProcessPath) ??
              (Icon)SystemIcons.Application.Clone()
            : (Icon)SystemIcons.Application.Clone();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _appIcon,
            Text = AppIdentity.Name,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(ShowRequested);
        _notifyIcon.MouseUp += NotifyIcon_MouseUp;
    }

    public event Action? ShowRequested;

    public event Action? MousePickRequested;

    public event Action? ExitRequested;

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.MouseUp -= NotifyIcon_MouseUp;
        _notifyIcon.Dispose();
        _trayMenu.ClosePermanently();
        _appIcon.Dispose();
    }

    private void Dispatch(Action? action)
    {
        if (action is null)
        {
            return;
        }

        _dispatcher.BeginInvoke(action);
    }

    private void NotifyIcon_MouseUp(
        object? sender,
        Forms.MouseEventArgs e)
    {
        if (e.Button != Forms.MouseButtons.Right)
        {
            return;
        }

        _dispatcher.BeginInvoke(_trayMenu.ShowAtCursor);
    }
}
