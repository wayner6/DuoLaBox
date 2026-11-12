using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DuoLaBox.Services;

public sealed class ImageResizeService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp",
            ".gif", ".tif", ".tiff"
        };

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public Task<ImageResizeResult> ResizeAsync(
        IEnumerable<string> paths,
        int width,
        int height,
        bool preserveAspectRatio)
    {
        var files = paths.ToArray();
        return Task.Run(
            () => Resize(files, width, height, preserveAspectRatio));
    }

    private static ImageResizeResult Resize(
        IEnumerable<string> paths,
        int width,
        int height,
        bool preserveAspectRatio)
    {
        if (width is < 1 or > 20000 ||
            height is < 1 or > 20000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "宽度和高度必须在 1–20000 之间。");
        }

        var outputs = new List<string>();
        var failures = new List<ImageResizeFailure>();
        foreach (var sourcePath in paths
                     .Where(IsSupported)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                outputs.Add(ResizeOne(
                    sourcePath,
                    width,
                    height,
                    preserveAspectRatio));
            }
            catch (Exception exception) when (
                exception is OutOfMemoryException or
                    System.Runtime.InteropServices.ExternalException or
                    IOException or UnauthorizedAccessException or
                    ArgumentException)
            {
                // 一张损坏或过大的图片不应中断整批；GDI+ 用 OutOfMemoryException 表示无法解码。
                failures.Add(new ImageResizeFailure(
                    sourcePath,
                    exception is OutOfMemoryException
                        ? "图片无法解码，或目标尺寸超出可用内存。"
                        : exception.Message));
            }
        }

        return new ImageResizeResult(outputs, failures);
    }

    private static string ResizeOne(
        string sourcePath,
        int width,
        int height,
        bool preserveAspectRatio)
    {
        string? outputPath = null;
        try
        {
            using var source = Image.FromFile(sourcePath);
            ApplyExifOrientation(source);
            var directory = Path.Combine(
                Path.GetDirectoryName(sourcePath)!,
                "百宝袋_调整尺寸");
            Directory.CreateDirectory(directory);
            outputPath = GetAvailableOutputPath(
                directory,
                Path.GetFileName(sourcePath));

            var pixelFormat =
                Path.GetExtension(sourcePath).Equals(
                    ".png",
                    StringComparison.OrdinalIgnoreCase)
                    ? PixelFormat.Format32bppArgb
                    : PixelFormat.Format24bppRgb;
            using var output = new Bitmap(width, height, pixelFormat);
            output.SetResolution(
                Math.Max(1, source.HorizontalResolution),
                Math.Max(1, source.VerticalResolution));
            using var graphics = Graphics.FromImage(output);
            graphics.Clear(
                pixelFormat == PixelFormat.Format32bppArgb
                    ? Color.Transparent
                    : Color.White);
            graphics.CompositingQuality =
                CompositingQuality.HighQuality;
            graphics.InterpolationMode =
                InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var destination = preserveAspectRatio
                ? CalculateFitRectangle(
                    source.Width,
                    source.Height,
                    width,
                    height)
                : new Rectangle(0, 0, width, height);
            graphics.DrawImage(source, destination);
            Save(output, outputPath);
            return outputPath;
        }
        catch
        {
            // 不留下写了一半的输出文件。
            if (outputPath is not null)
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            throw;
        }
    }

    // 手机竖拍的照片像素本身是横的，靠 EXIF 方向标记旋转显示；
    // 重新绘制会丢掉这个标记，所以先把像素转正。
    private static void ApplyExifOrientation(Image image)
    {
        const int orientationId = 0x0112;
        if (!image.PropertyIdList.Contains(orientationId))
        {
            return;
        }

        var value = image.GetPropertyItem(orientationId)?.Value;
        var flip = GetOrientationFlip(
            value is { Length: >= 2 } ? BitConverter.ToUInt16(value, 0) : 1);
        if (flip != RotateFlipType.RotateNoneFlipNone)
        {
            image.RotateFlip(flip);
        }

        image.RemovePropertyItem(orientationId);
    }

    internal static RotateFlipType GetOrientationFlip(int orientation) =>
        orientation switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        };

    private static Rectangle CalculateFitRectangle(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        var scale = Math.Min(
            targetWidth / (double)sourceWidth,
            targetHeight / (double)sourceHeight);
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return new Rectangle(
            (targetWidth - width) / 2,
            (targetHeight - height) / 2,
            width,
            height);
    }

    private static void Save(Bitmap image, string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        image.Save(path, extension switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            _ => ImageFormat.Png
        });
    }

    private static string GetAvailableOutputPath(
        string directory,
        string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index < int.MaxValue; index++)
        {
            candidate = Path.Combine(
                directory,
                $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法生成可用的输出文件名。");
    }
}

public sealed record ImageResizeResult(
    IReadOnlyList<string> Outputs,
    IReadOnlyList<ImageResizeFailure> Failures);

public sealed record ImageResizeFailure(string Path, string Reason);
