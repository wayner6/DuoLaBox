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

    public Task<IReadOnlyList<string>> ResizeAsync(
        IEnumerable<string> paths,
        int width,
        int height,
        bool preserveAspectRatio)
    {
        var files = paths.ToArray();
        return Task.Run<IReadOnlyList<string>>(
            () => Resize(files, width, height, preserveAspectRatio));
    }

    private static IReadOnlyList<string> Resize(
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
        foreach (var sourcePath in paths
                     .Where(IsSupported)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var source = Image.FromFile(sourcePath);
            var directory = Path.Combine(
                Path.GetDirectoryName(sourcePath)!,
                "百宝袋_调整尺寸");
            Directory.CreateDirectory(directory);
            var outputPath = GetAvailableOutputPath(
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
            outputs.Add(outputPath);
        }

        return outputs;
    }

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
