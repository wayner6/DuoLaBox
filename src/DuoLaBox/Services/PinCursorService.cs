using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DuoLaBox.Services;

public sealed class PinCursorService : IDisposable
{
    private bool _isApplied;

    public PinCursorService()
    {
        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
    }

    public bool Apply()
    {
        Restore();

        var cursor = CreatePinCursor();
        if (cursor == nint.Zero)
        {
            return false;
        }

        if (!NativeMethods.SetSystemCursor(
                cursor,
                NativeMethods.OcrNormal))
        {
            NativeMethods.DestroyCursor(cursor);
            return false;
        }

        // SetSystemCursor 接管并销毁传入句柄。
        _isApplied = true;
        return true;
    }

    public void Restore()
    {
        if (!_isApplied)
        {
            return;
        }

        NativeMethods.SystemParametersInfo(
            NativeMethods.SpiSetCursors,
            0,
            nint.Zero,
            0);
        _isApplied = false;
    }

    public void Dispose()
    {
        Restore();
        AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
    }

    private static nint CreatePinCursor()
    {
        using var bitmap = new Bitmap(
            32,
            32,
            PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var whiteFill = new SolidBrush(Color.White);
            using var blueFill = new SolidBrush(
                Color.FromArgb(255, 0, 103, 192));
            using var whitePen = new Pen(Color.White, 5)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var bluePen = new Pen(
                Color.FromArgb(255, 0, 103, 192),
                3)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            graphics.DrawLine(whitePen, 16, 15, 16, 29);
            graphics.DrawLine(bluePen, 16, 15, 16, 29);

            var outerBody = new[]
            {
                new PointF(8, 8),
                new PointF(24, 8),
                new PointF(20, 18),
                new PointF(12, 18)
            };
            var innerBody = new[]
            {
                new PointF(10, 9),
                new PointF(22, 9),
                new PointF(18.7f, 16),
                new PointF(13.3f, 16)
            };
            graphics.FillPolygon(whiteFill, outerBody);
            graphics.FillPolygon(blueFill, innerBody);
            graphics.FillEllipse(whiteFill, 6, 2, 20, 10);
            graphics.FillEllipse(blueFill, 8, 4, 16, 6);
        }

        var icon = bitmap.GetHicon();
        if (!NativeMethods.GetIconInfo(icon, out var iconInfo))
        {
            NativeMethods.DestroyIcon(icon);
            return nint.Zero;
        }

        iconInfo.IsIcon = false;
        iconInfo.HotspotX = 16;
        iconInfo.HotspotY = 30;
        var cursor = NativeMethods.CreateIconIndirect(ref iconInfo);

        NativeMethods.DeleteObject(iconInfo.MaskBitmap);
        NativeMethods.DeleteObject(iconInfo.ColorBitmap);
        NativeMethods.DestroyIcon(icon);
        return cursor;
    }

    private void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
        Restore();
    }
}
