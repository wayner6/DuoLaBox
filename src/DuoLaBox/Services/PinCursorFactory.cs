using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DuoLaBox.Services;

internal static class PinCursorFactory
{
    // 调用方负责用 DestroyCursor 释放返回的句柄；失败时返回 0。
    internal static nint Create()
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
}
