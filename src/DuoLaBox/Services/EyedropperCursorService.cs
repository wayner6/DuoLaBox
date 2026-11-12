using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DuoLaBox.Services;

public sealed class EyedropperCursorService : IDisposable
{
    private static readonly uint[] CursorIds =
    [
        NativeMethods.OcrNormal,
        NativeMethods.OcrIBeam,
        NativeMethods.OcrWait,
        NativeMethods.OcrCross,
        NativeMethods.OcrUp,
        NativeMethods.OcrSizeNwSe,
        NativeMethods.OcrSizeNeSw,
        NativeMethods.OcrSizeWe,
        NativeMethods.OcrSizeNs,
        NativeMethods.OcrSizeAll,
        NativeMethods.OcrNo,
        NativeMethods.OcrHand,
        NativeMethods.OcrAppStarting,
        NativeMethods.OcrHelp
    ];

    private bool _isApplied;

    public EyedropperCursorService()
    {
        AppDomain.CurrentDomain.ProcessExit +=
            CurrentDomain_ProcessExit;
    }

    private static string MarkerPath =>
        AppIdentity.GetLocalDataPath("cursor-override.marker");

    // 取色必须读取屏幕上未经遮挡的原始像素，所以仍然替换系统指针。
    // 指针替换期间留一个标记文件：进程被强行结束后，下次启动据此恢复系统指针。
    public static void RecoverAfterAbnormalExit()
    {
        if (!File.Exists(MarkerPath))
        {
            return;
        }

        RestoreSystemCursors();
        TryDeleteMarker();
    }

    public bool Apply()
    {
        Restore();
        TryWriteMarker();
        foreach (var cursorId in CursorIds)
        {
            var cursor = CreateCursor();
            if (cursor == nint.Zero)
            {
                RestoreSystemCursors();
                TryDeleteMarker();
                return false;
            }

            if (!NativeMethods.SetSystemCursor(cursor, cursorId))
            {
                NativeMethods.DestroyCursor(cursor);
                RestoreSystemCursors();
                TryDeleteMarker();
                return false;
            }
        }

        _isApplied = true;
        return true;
    }

    public void Restore()
    {
        if (!_isApplied)
        {
            return;
        }

        RestoreSystemCursors();
        TryDeleteMarker();
        _isApplied = false;
    }

    private static void TryWriteMarker()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, string.Empty);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // 没有标记只是少一层崩溃后的恢复，不影响取色。
        }
    }

    private static void TryDeleteMarker()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // 残留的标记只会让下次启动多恢复一次系统指针。
        }
    }

    public void Dispose()
    {
        Restore();
        AppDomain.CurrentDomain.ProcessExit -=
            CurrentDomain_ProcessExit;
    }

    private static nint CreateCursor()
    {
        using var bitmap = new Bitmap(
            32,
            32,
            PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var whitePen = new Pen(Color.White, 5)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var darkPen = new Pen(Color.FromArgb(255, 34, 34, 34), 3)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var bluePen = new Pen(Color.FromArgb(255, 0, 120, 212), 5)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var whiteFill = new SolidBrush(Color.White);
            using var darkFill = new SolidBrush(Color.FromArgb(255, 34, 34, 34));

            graphics.DrawLine(whitePen, 8, 25, 22, 11);
            graphics.DrawLine(darkPen, 8, 25, 22, 11);
            graphics.DrawLine(bluePen, 12, 21, 20, 13);
            graphics.FillEllipse(whiteFill, 18, 4, 11, 11);
            graphics.FillEllipse(darkFill, 20, 6, 7, 7);
            graphics.DrawLine(whitePen, 5, 28, 9, 24);
            graphics.DrawLine(darkPen, 5, 28, 9, 24);
        }

        var icon = bitmap.GetHicon();
        if (!NativeMethods.GetIconInfo(icon, out var iconInfo))
        {
            NativeMethods.DestroyIcon(icon);
            return nint.Zero;
        }

        iconInfo.IsIcon = false;
        iconInfo.HotspotX = 5;
        iconInfo.HotspotY = 28;
        var cursor = NativeMethods.CreateIconIndirect(ref iconInfo);
        NativeMethods.DeleteObject(iconInfo.MaskBitmap);
        NativeMethods.DeleteObject(iconInfo.ColorBitmap);
        NativeMethods.DestroyIcon(icon);
        return cursor;
    }

    private static void RestoreSystemCursors()
    {
        NativeMethods.SystemParametersInfo(
            NativeMethods.SpiSetCursors,
            0,
            nint.Zero,
            0);
    }

    private void CurrentDomain_ProcessExit(
        object? sender,
        EventArgs e)
    {
        Restore();
    }
}
