using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DuoLaBox.Services;

public sealed class WindowPickerService : IDisposable
{
    private const double ClickableTopBandHeight = 56;

    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.LowLevelMouseProc _mouseHookCallback;
    private readonly PinCursorService _pinCursor = new();
    private nint _mouseHook;
    private NativeMethods.NativePoint _pendingClickPoint;
    private bool _hasPendingClick;

    public WindowPickerService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHookCallback = MouseHookCallback;
    }

    public event Action<nint>? WindowPicked;

    public event Action? InvalidWindowTopClick;

    public bool IsActive => _mouseHook != nint.Zero;

    public void Start()
    {
        Cancel();
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseHookCallback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_mouseHook != nint.Zero)
        {
            _pinCursor.Apply();
        }
    }

    public void Cancel()
    {
        if (_mouseHook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
        }

        _pinCursor.Restore();
        _hasPendingClick = false;
    }

    public void Dispose()
    {
        Cancel();
        _pinCursor.Dispose();
    }

    private nint MouseHookCallback(
        int code,
        nint message,
        nint hookData)
    {
        if (code >= 0 && IsActive)
        {
            var data = Marshal.PtrToStructure<NativeMethods.LowLevelMouseData>(
                hookData);

            if (message == (nint)NativeMethods.WmLButtonDown)
            {
                _pendingClickPoint = data.Point;
                _hasPendingClick = true;
            }
            else if (message == (nint)NativeMethods.WmLButtonUp &&
                     _hasPendingClick)
            {
                var point = _pendingClickPoint;
                _hasPendingClick = false;
                _dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    () => ProcessClick(point));
            }
        }

        return NativeMethods.CallNextHookEx(
            _mouseHook,
            code,
            message,
            hookData);
    }

    private void ProcessClick(NativeMethods.NativePoint point)
    {
        if (!IsActive)
        {
            return;
        }

        var handle = GetWindowUnderCursor(point);
        if (handle == nint.Zero || !IsClickInWindowTopBand(handle, point))
        {
            InvalidWindowTopClick?.Invoke();
            return;
        }

        Cancel();
        WindowPicked?.Invoke(handle);
    }

    private static nint GetWindowUnderCursor(NativeMethods.NativePoint point)
    {
        var handle = NativeMethods.WindowFromPoint(point);
        if (handle == nint.Zero)
        {
            return nint.Zero;
        }

        handle = NativeMethods.GetAncestor(handle, NativeMethods.GaRoot);
        if (handle == nint.Zero || !NativeMethods.IsWindowVisible(handle))
        {
            return nint.Zero;
        }

        if (handle == NativeMethods.GetShellWindow())
        {
            return nint.Zero;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return nint.Zero;
        }

        return NativeMethods.GetWindowTextLength(handle) > 0
            ? handle
            : nint.Zero;
    }

    private static bool IsClickInWindowTopBand(
        nint handle,
        NativeMethods.NativePoint point)
    {
        if (!NativeMethods.GetWindowRect(handle, out var rectangle) ||
            rectangle.Width <= 0 ||
            rectangle.Height <= 0)
        {
            return false;
        }

        var dpi = NativeMethods.GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var bandHeight = (int)Math.Round(ClickableTopBandHeight * scale);

        return point.X >= rectangle.Left &&
               point.X < rectangle.Right &&
               point.Y >= rectangle.Top &&
               point.Y <= rectangle.Top + bandHeight;
    }
}
