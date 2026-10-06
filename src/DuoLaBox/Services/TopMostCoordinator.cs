using DuoLaBox.Models;

namespace DuoLaBox.Services;

public sealed class TopMostCoordinator : IDisposable
{
    private readonly IWindowService _windowService;
    private readonly TopMostMarkerManager _markerManager;

    public TopMostCoordinator()
    {
        _windowService = new WindowService();
        _markerManager = new TopMostMarkerManager();
        _markerManager.UnpinRequested += MarkerManager_UnpinRequested;
    }

    public event Action<nint>? MarkerUnpinned;

    public event Action<nint>? MarkerUnpinFailed;

    public IReadOnlyList<WindowInfo> GetOpenWindows() =>
        _windowService.GetOpenWindows();

    public bool SetTopMost(nint handle, bool topMost)
    {
        if (!_windowService.SetTopMost(handle, topMost))
        {
            return false;
        }

        if (topMost)
        {
            _markerManager.AddMarker(handle);
        }
        else
        {
            _markerManager.RemoveMarker(handle);
        }

        return true;
    }

    public void Dispose()
    {
        _markerManager.UnpinRequested -= MarkerManager_UnpinRequested;
        _markerManager.Dispose();
    }

    private void MarkerManager_UnpinRequested(nint handle)
    {
        if (!SetTopMost(handle, false))
        {
            MarkerUnpinFailed?.Invoke(handle);
            return;
        }

        MarkerUnpinned?.Invoke(handle);
    }
}
