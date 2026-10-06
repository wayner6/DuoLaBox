using System.Diagnostics;
using System.Text;
using DuoLaBox.Models;

namespace DuoLaBox.Services;

public sealed class WindowService : IWindowService
{
    private readonly WindowIconService _windowIconService = new();

    private static readonly HashSet<string> IgnoredInfrastructureProcesses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "TextInputHost",
            "StartMenuExperienceHost",
            "ShellExperienceHost",
            "SearchHost",
            "SearchApp",
            "LockApp"
        };

    public IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle))
            {
                return true;
            }

            var titleLength = NativeMethods.GetWindowTextLength(handle);
            if (titleLength <= 0)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0)
            {
                return true;
            }

            var titleBuilder = new StringBuilder(titleLength + 1);
            NativeMethods.GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString().Trim();
            if (title.Length == 0)
            {
                return true;
            }

            if (!IsUserManageableWindow(handle))
            {
                return true;
            }

            var processName = TryGetProcessName(processId);
            if (IgnoredInfrastructureProcesses.Contains(processName))
            {
                return true;
            }

            var extendedStyle = NativeMethods.GetWindowLongPtr(
                handle,
                NativeMethods.GwlExStyle).ToInt64();

            windows.Add(new WindowInfo
            {
                Icon = _windowIconService.GetIcon(handle, processId),
                Handle = handle,
                Title = title,
                ProcessId = processId,
                ProcessName = processName,
                IsTopMost = (extendedStyle & NativeMethods.WsExTopMost) != 0
            });

            return true;
        }, nint.Zero);

        _windowIconService.Prune(
            windows.Select(window => window.ProcessId).ToHashSet());

        return windows
            .OrderByDescending(window => window.IsTopMost)
            .ThenBy(window => window.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public bool SetTopMost(nint handle, bool topMost)
    {
        var wasMinimized = topMost && NativeMethods.IsIconic(handle);
        if (wasMinimized)
        {
            NativeMethods.ShowWindowAsync(handle, NativeMethods.SwRestore);
        }

        var insertAfter = topMost
            ? NativeMethods.HwndTopMost
            : NativeMethods.HwndNoTopMost;

        var succeeded = NativeMethods.SetWindowPos(
            handle,
            insertAfter,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoActivate);

        if (succeeded && wasMinimized)
        {
            NativeMethods.SetForegroundWindow(handle);
        }

        return succeeded;
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsUserManageableWindow(nint handle)
    {
        if (handle == NativeMethods.GetShellWindow())
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(
                handle,
                NativeMethods.DwmwaCloaked,
                out var cloaked,
                sizeof(int)) == 0 &&
            cloaked != 0)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(handle, out var rectangle) ||
            rectangle.Width <= 2 ||
            rectangle.Height <= 2)
        {
            return false;
        }

        var classNameBuilder = new StringBuilder(256);
        NativeMethods.GetClassName(
            handle,
            classNameBuilder,
            classNameBuilder.Capacity);
        var className = classNameBuilder.ToString();

        if (className is "Progman" or
            "WorkerW" or
            "Shell_TrayWnd" or
            "Shell_SecondaryTrayWnd" or
            "Windows.UI.Core.CoreWindow")
        {
            return false;
        }

        // 文件资源管理器必须保留，即便未来系统版本调整其窗口样式。
        if (className is "CabinetWClass" or "ExploreWClass")
        {
            return true;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle).ToInt64();
        var isToolWindow =
            (extendedStyle & NativeMethods.WsExToolWindow) != 0;
        var isAppWindow =
            (extendedStyle & NativeMethods.WsExAppWindow) != 0;

        if (isToolWindow && !isAppWindow)
        {
            return false;
        }

        var owner = NativeMethods.GetWindow(handle, NativeMethods.GwOwner);
        return owner == nint.Zero || isAppWindow;
    }
}
