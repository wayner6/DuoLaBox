using System.Collections.Concurrent;
using System.Windows.Threading;
using DuoLaBox.Views;

namespace DuoLaBox.Services;

public sealed class TopMostMarkerManager : IDisposable
{
    private const double MarkerWidth = 38;
    private const double MarkerHeight = 24;

    private readonly Dictionary<nint, TopMostMarkerWindow> _markers = [];
    private readonly ConcurrentDictionary<nint, byte> _trackedTargets = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.WinEventProc _winEventCallback;
    private readonly nint _locationHook;
    private readonly nint _minimizeHook;
    private int _positionUpdatePending;

    public TopMostMarkerManager()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _winEventCallback = WinEventCallback;
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _positionTimer.Tick += PositionTimer_Tick;

        _locationHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectLocationChange,
            NativeMethods.EventObjectLocationChange,
            nint.Zero,
            _winEventCallback,
            0,
            0,
            NativeMethods.WineventOutofcontext);
        _minimizeHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemMinimizeStart,
            NativeMethods.EventSystemMinimizeEnd,
            nint.Zero,
            _winEventCallback,
            0,
            0,
            NativeMethods.WineventOutofcontext);
    }

    public event Action<nint>? UnpinRequested;

    public void AddMarker(nint targetHandle)
    {
        if (_markers.TryGetValue(targetHandle, out var existingMarker))
        {
            PositionMarker(existingMarker);
            return;
        }

        var marker = new TopMostMarkerWindow(targetHandle);
        marker.UnpinRequested += Marker_UnpinRequested;
        marker.Show();
        _markers[targetHandle] = marker;
        _trackedTargets[targetHandle] = 0;
        UpdateMarker(marker);

        if (!_positionTimer.IsEnabled)
        {
            _positionTimer.Start();
        }
    }

    public void RemoveMarker(nint targetHandle)
    {
        if (!_markers.Remove(targetHandle, out var marker))
        {
            return;
        }

        marker.UnpinRequested -= Marker_UnpinRequested;
        marker.Close();
        _trackedTargets.TryRemove(targetHandle, out _);

        if (_markers.Count == 0)
        {
            _positionTimer.Stop();
        }
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        _positionTimer.Tick -= PositionTimer_Tick;
        if (_locationHook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_locationHook);
        }

        if (_minimizeHook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_minimizeHook);
        }

        foreach (var marker in _markers.Values.ToArray())
        {
            marker.UnpinRequested -= Marker_UnpinRequested;
            marker.Close();
        }

        _markers.Clear();
        _trackedTargets.Clear();
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        UpdateAllMarkers();
    }

    private void UpdateAllMarkers()
    {
        foreach (var marker in _markers.Values.ToArray())
        {
            UpdateMarker(marker);
        }
    }

    private void UpdateMarker(TopMostMarkerWindow marker)
    {
        if (!NativeMethods.IsWindow(marker.TargetHandle) ||
            !IsTopMost(marker.TargetHandle))
        {
            RemoveMarker(marker.TargetHandle);
            return;
        }

        if (!NativeMethods.IsWindowVisible(marker.TargetHandle) ||
            NativeMethods.IsIconic(marker.TargetHandle))
        {
            marker.Hide();
            return;
        }

        if (!marker.IsVisible)
        {
            marker.Show();
        }

        PositionMarker(marker);
    }

    private static void PositionMarker(TopMostMarkerWindow marker)
    {
        if (!NativeMethods.GetWindowRect(marker.TargetHandle, out var rectangle) ||
            rectangle.Width <= 0)
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(marker.TargetHandle);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var width = (int)Math.Round(MarkerWidth * scale);
        var height = (int)Math.Round(MarkerHeight * scale);
        var x = rectangle.Left + ((rectangle.Width - width) / 2);
        var y = rectangle.Top + (int)Math.Round(3 * scale);

        NativeMethods.SetWindowPos(
            marker.MarkerHandle,
            NativeMethods.HwndTopMost,
            x,
            y,
            width,
            height,
            NativeMethods.SwpNoActivate);
    }

    private static bool IsTopMost(nint handle)
    {
        var extendedStyle = NativeMethods.GetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle).ToInt64();

        return (extendedStyle & NativeMethods.WsExTopMost) != 0;
    }

    private void Marker_UnpinRequested(nint targetHandle)
    {
        UnpinRequested?.Invoke(targetHandle);
    }

    private void WinEventCallback(
        nint hook,
        uint eventType,
        nint hWnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (hWnd == nint.Zero ||
            (eventType == NativeMethods.EventObjectLocationChange &&
             objectId != NativeMethods.ObjidWindow) ||
            !_trackedTargets.ContainsKey(hWnd))
        {
            return;
        }

        if (Interlocked.Exchange(ref _positionUpdatePending, 1) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            () =>
            {
                Interlocked.Exchange(ref _positionUpdatePending, 0);
                UpdateAllMarkers();
            });
    }
}
