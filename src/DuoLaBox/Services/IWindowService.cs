using DuoLaBox.Models;

namespace DuoLaBox.Services;

public interface IWindowService
{
    IReadOnlyList<WindowInfo> GetOpenWindows();

    bool SetTopMost(nint handle, bool topMost);
}
