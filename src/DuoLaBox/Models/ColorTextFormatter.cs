using System.Globalization;
using MediaColor = System.Windows.Media.Color;

namespace DuoLaBox.Models;

public static class ColorTextFormatter
{
    public static string ToHex(MediaColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string ToRgb(MediaColor color) =>
        $"rgb({color.R}, {color.G}, {color.B})";

    public static string ToHsl(MediaColor color)
    {
        var (hue, saturation, lightness) = GetHsl(color);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"hsl({hue:0}, {saturation:0}%, {lightness:0}%)");
    }

    public static string ToHsv(MediaColor color)
    {
        var (hue, saturation, value) = GetHsv(color);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"hsv({hue:0}, {saturation:0}%, {value:0}%)");
    }

    public static IReadOnlyList<ColorFormatItem> CreateFormats(
        MediaColor color) =>
    [
        new("HEX", ToHex(color)),
        new("RGB", ToRgb(color)),
        new("HSL", ToHsl(color)),
        new("HSV", ToHsv(color))
    ];

    private static (double Hue, double Saturation, double Lightness)
        GetHsl(MediaColor color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        var lightness = (maximum + minimum) / 2d;
        var saturation = delta == 0
            ? 0
            : delta / (1 - Math.Abs(2 * lightness - 1));
        return (
            CalculateHue(red, green, blue, maximum, delta),
            saturation * 100,
            lightness * 100);
    }

    private static (double Hue, double Saturation, double Value)
        GetHsv(MediaColor color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        return (
            CalculateHue(red, green, blue, maximum, delta),
            maximum == 0 ? 0 : delta / maximum * 100,
            maximum * 100);
    }

    private static double CalculateHue(
        double red,
        double green,
        double blue,
        double maximum,
        double delta)
    {
        if (delta == 0)
        {
            return 0;
        }

        var hue = maximum == red
            ? 60 * (((green - blue) / delta) % 6)
            : maximum == green
                ? 60 * (((blue - red) / delta) + 2)
                : 60 * (((red - green) / delta) + 4);
        return hue < 0 ? hue + 360 : hue;
    }
}
