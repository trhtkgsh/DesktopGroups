using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DesktopGroups;

// Colors are stored as "#RRGGBB" text in the settings file and turned into brushes when they are shown.
public static class ColorMath
{
    public const string DefaultHex = "#795CF5";

    public static Color ToColor(string hex) =>
        ColorConverter.ConvertFromString(hex) is Color color
            ? color
            : throw new FormatException($"Invalid color: {hex}");

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color HslToColor(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var sector = hue / 60;
        var x = chroma * (1 - Math.Abs(sector % 2 - 1));
        var m = lightness - chroma / 2;
        var (r, g, b) = sector switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        var delta = max - min;
        if (delta < 1e-9) return (0, 0, lightness);

        var saturation = delta / Math.Max(1e-9, 1 - Math.Abs(2 * lightness - 1));
        var hue = max == r ? 60 * ((g - b) / delta % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return (hue, saturation, lightness);
    }

    // Mixes the color with white: 0 keeps the color, 1 gives white.
    public static Color LightenTowardsWhite(Color color, double ratio) =>
        Color.FromRgb(Mix(color.R, ratio), Mix(color.G, ratio), Mix(color.B, ratio));

    // Dark text on light colors, white text on dark colors.
    public static Brush TextBrushFor(Color color)
    {
        var luma = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        var brush = new SolidColorBrush(luma > 160 ? Color.FromRgb(0x1F, 0x1F, 0x1F) : Colors.White);
        brush.Freeze();
        return brush;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private static byte Mix(byte channel, double ratio) => (byte)Math.Round(channel + (255 - channel) * ratio);
}

// Shows a "#RRGGBB" value as a brush. With the parameter "text" it returns a readable text brush for that color.
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            var color = ColorMath.ToColor(value as string ?? "");
            if (parameter as string == "text") return ColorMath.TextBrushFor(color);

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
