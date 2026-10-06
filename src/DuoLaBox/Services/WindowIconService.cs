using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DuoLaBox.Services;

public sealed class WindowIconService
{
    private readonly Dictionary<uint, ImageSource> _iconCache = [];

    public ImageSource? GetIcon(nint window, uint processId)
    {
        if (_iconCache.TryGetValue(processId, out var cached))
        {
            return cached;
        }

        var icon =
            TryGetProcessIcon(processId) ??
            CreateImageSource(TryGetWindowIcon(window));
        if (icon is not null)
        {
            _iconCache[processId] = icon;
        }

        return icon;
    }

    public void Prune(IReadOnlySet<uint> activeProcessIds)
    {
        foreach (var processId in _iconCache.Keys.ToArray())
        {
            if (!activeProcessIds.Contains(processId))
            {
                _iconCache.Remove(processId);
            }
        }
    }

    private static nint TryGetWindowIcon(nint window)
    {
        foreach (var iconType in new[]
                 {
                     NativeMethods.IconBig,
                     NativeMethods.IconSmall2,
                     NativeMethods.IconSmall
                 })
        {
            NativeMethods.SendMessageTimeoutHandle(
                window,
                NativeMethods.WmGetIcon,
                iconType,
                nint.Zero,
                NativeMethods.SmtoAbortIfHung,
                30,
                out var result);
            if (result != 0)
            {
                return new nint(unchecked((long)result));
            }
        }

        var classIcon = NativeMethods.GetClassLongPtr(
            window,
            NativeMethods.GclpHiconSmall);
        return classIcon != nint.Zero
            ? classIcon
            : NativeMethods.GetClassLongPtr(
                window,
                NativeMethods.GclpHicon);
    }

    private static ImageSource? TryGetProcessIcon(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return null;
            }

            var shellIcon = TryGetShellIcon(executablePath);
            if (shellIcon is not null)
            {
                return shellIcon;
            }

            using var icon =
                System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            return icon is null
                ? null
                : CreateImageSource(icon.Handle);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryGetShellIcon(string executablePath)
    {
        NativeMethods.IImageList? imageList = null;
        try
        {
            var fileInfoSize = (uint)Marshal.SizeOf<
                NativeMethods.ShellFileInfo>();
            if (NativeMethods.SHGetFileInfo(
                    executablePath,
                    0,
                    out var fileInfo,
                    fileInfoSize,
                    NativeMethods.ShgfiSysiconindex) == nint.Zero)
            {
                return null;
            }

            var interfaceId =
                typeof(NativeMethods.IImageList).GUID;
            if (NativeMethods.SHGetImageList(
                    NativeMethods.ShilJumbo,
                    ref interfaceId,
                    out imageList) != 0 ||
                imageList.GetIcon(
                    fileInfo.IconIndex,
                    NativeMethods.IldTransparent,
                    out var icon) != 0 ||
                icon == nint.Zero)
            {
                return null;
            }

            try
            {
                return CreateImageSource(icon);
            }
            finally
            {
                NativeMethods.DestroyIcon(icon);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (imageList is not null &&
                Marshal.IsComObject(imageList))
            {
                Marshal.ReleaseComObject(imageList);
            }
        }
    }

    private static ImageSource? CreateImageSource(nint iconHandle)
    {
        if (iconHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
