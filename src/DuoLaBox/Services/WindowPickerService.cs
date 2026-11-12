using Forms = System.Windows.Forms;

namespace DuoLaBox.Services;

public sealed class WindowPickerService : IDisposable
{
    private PickOverlayForm? _overlay;
    private Forms.Cursor? _cursor;
    private nint _cursorHandle;

    public event Action<nint>? WindowPicked;

    public event Action? InvalidWindowClick;

    // 用户在覆盖窗口上按 Esc 或点击右键。
    public event Action? Cancelled;

    public bool IsActive => _overlay is not null;

    public void Start()
    {
        Cancel();
        _cursorHandle = PinCursorFactory.Create();
        _cursor = _cursorHandle != nint.Zero
            ? new Forms.Cursor(_cursorHandle)
            : null;

        var overlay = new PickOverlayForm(_cursor ?? Forms.Cursors.Cross);
        overlay.PointClicked += Overlay_PointClicked;
        overlay.CancelRequested += Overlay_CancelRequested;
        _overlay = overlay;
        overlay.Show();
        overlay.Activate();
    }

    public void Cancel()
    {
        var overlay = _overlay;
        _overlay = null;
        if (overlay is not null)
        {
            overlay.PointClicked -= Overlay_PointClicked;
            overlay.CancelRequested -= Overlay_CancelRequested;
            overlay.Close();
            overlay.Dispose();
        }

        _cursor?.Dispose();
        _cursor = null;
        if (_cursorHandle != nint.Zero)
        {
            NativeMethods.DestroyCursor(_cursorHandle);
            _cursorHandle = nint.Zero;
        }
    }

    public void Dispose()
    {
        Cancel();
    }

    private void Overlay_PointClicked(System.Drawing.Point point)
    {
        var overlay = _overlay;
        if (overlay is null)
        {
            return;
        }

        // 先隐藏覆盖窗口，才能查到它下面的窗口。
        overlay.Hide();
        var handle = GetWindowAt(new NativeMethods.NativePoint
        {
            X = point.X,
            Y = point.Y
        });
        if (handle == nint.Zero)
        {
            overlay.Show();
            overlay.Activate();
            InvalidWindowClick?.Invoke();
            return;
        }

        Cancel();
        WindowPicked?.Invoke(handle);
    }

    private void Overlay_CancelRequested()
    {
        Cancel();
        Cancelled?.Invoke();
    }

    private static nint GetWindowAt(NativeMethods.NativePoint point)
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
}
