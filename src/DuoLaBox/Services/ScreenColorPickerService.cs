using System.Runtime.InteropServices;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;

namespace DuoLaBox.Services;

public sealed record ScreenColorSample(
    MediaColor Color,
    int ScreenX,
    int ScreenY);

public sealed class ScreenColorPickerService : IDisposable
{
    private const uint InvalidColor = 0xFFFFFFFF;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _previewTimer;
    private readonly NativeMethods.LowLevelMouseProc _mouseCallback;
    private readonly EyedropperCursorService _cursor = new();
    private NativeMethods.NativePoint _latestPoint;
    private bool _hasLatestPoint;
    private nint _mouseHook;

    public ScreenColorPickerService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _mouseCallback = MouseHookCallback;
        _previewTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(32),
            DispatcherPriority.Render,
            PreviewTimer_Tick,
            _dispatcher);
        _previewTimer.Stop();
    }

    public event Action<MediaColor>? ColorPicked;

    public event Action<ScreenColorSample>? PreviewChanged;

    public event Action? PickingFailed;

    public bool IsActive => _mouseHook != nint.Zero;

    public bool Start()
    {
        Cancel();
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseCallback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_mouseHook == nint.Zero)
        {
            return false;
        }

        if (!_cursor.Apply())
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
            return false;
        }
        _hasLatestPoint =
            NativeMethods.GetCursorPos(out _latestPoint);
        _previewTimer.Start();
        PublishPreview();
        return true;
    }

    public void Cancel()
    {
        _previewTimer.Stop();
        if (_mouseHook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = nint.Zero;
        }

        _cursor.Restore();
        _hasLatestPoint = false;
    }

    public void Dispose()
    {
        Cancel();
        _previewTimer.Tick -= PreviewTimer_Tick;
        _cursor.Dispose();
    }

    private nint MouseHookCallback(
        int code,
        nint message,
        nint hookData)
    {
        if (code < 0 || !IsActive)
        {
            return NativeMethods.CallNextHookEx(
                _mouseHook,
                code,
                message,
                hookData);
        }

        var data =
            Marshal.PtrToStructure<NativeMethods.LowLevelMouseData>(
                hookData);
        if (message == (nint)NativeMethods.WmMouseMove)
        {
            _latestPoint = data.Point;
            _hasLatestPoint = true;
        }
        else if (message == (nint)NativeMethods.WmLButtonUp)
        {
            var point = data.Point;
            Cancel();
            _dispatcher.BeginInvoke(() => PublishPickedColor(point));
            return new nint(1);
        }
        else if (message == (nint)NativeMethods.WmLButtonDown)
        {
            return new nint(1);
        }

        return NativeMethods.CallNextHookEx(
            _mouseHook,
            code,
            message,
            hookData);
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        PublishPreview();
    }

    private void PublishPreview()
    {
        if (!_hasLatestPoint ||
            !TrySampleColor(_latestPoint, out var color))
        {
            return;
        }

        PreviewChanged?.Invoke(new ScreenColorSample(
            color,
            _latestPoint.X,
            _latestPoint.Y));
    }

    private void PublishPickedColor(NativeMethods.NativePoint point)
    {
        if (TrySampleColor(point, out var color))
        {
            ColorPicked?.Invoke(color);
            return;
        }

        PickingFailed?.Invoke();
    }

    private static bool TrySampleColor(
        NativeMethods.NativePoint point,
        out MediaColor color)
    {
        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            color = default;
            return false;
        }

        try
        {
            var colorReference =
                NativeMethods.GetPixel(screenDc, point.X, point.Y);
            if (colorReference == InvalidColor)
            {
                color = default;
                return false;
            }

            color = MediaColor.FromRgb(
                (byte)(colorReference & 0xFF),
                (byte)((colorReference >> 8) & 0xFF),
                (byte)((colorReference >> 16) & 0xFF));
            return true;
        }
        finally
        {
            NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }
}
