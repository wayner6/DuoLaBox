using System.Windows.Media;

namespace DuoLaBox.Models;

public sealed record ColorFormatItem(string Name, string Value);

public sealed class FavoriteColorItem
{
    public FavoriteColorItem(string hex)
    {
        Hex = hex;
        var color = (System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(hex);
        Brush = new SolidColorBrush(color);
        Rgb = ColorTextFormatter.ToRgb(color);
        Hsl = ColorTextFormatter.ToHsl(color);
        Hsv = ColorTextFormatter.ToHsv(color);
    }

    public string Hex { get; }

    public string Rgb { get; }

    public string Hsl { get; }

    public string Hsv { get; }

    public System.Windows.Media.Brush Brush { get; }
}
