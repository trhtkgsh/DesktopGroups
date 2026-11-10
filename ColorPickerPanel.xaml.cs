using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopGroups;

// Color palette shown inside the manager window, in a dropdown. Every click reports the new color at once.
public partial class ColorPickerPanel : UserControl
{
    private const int CanvasWidth = 300;
    private const int CanvasHeight = 200;
    private const double Saturation = 0.7;
    private const double TopLightness = 0.9;
    private const double BottomLightness = 0.2;

    private static readonly BitmapSource Palette = CreatePalette();
    private Action<string> changed = _ => { };

    public ColorPickerPanel()
    {
        InitializeComponent();
        PaletteImage.Source = Palette;
    }

    // Shows the current color on the palette and reports every change to the given action.
    public void Show(string currentHex, Action<string> onChanged)
    {
        changed = onChanged;

        var (hue, _, lightness) = ColorMath.ToHsl(ColorMath.ToColor(currentHex));
        MoveMarker(hue / 360 * (CanvasWidth - 1), (TopLightness - lightness) / (TopLightness - BottomLightness) * (CanvasHeight - 1));
        ShowColor(currentHex);
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        PaletteCanvas.CaptureMouse();
        PickAt(e.GetPosition(PaletteCanvas));
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (PaletteCanvas.IsMouseCaptured) PickAt(e.GetPosition(PaletteCanvas));
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e) => PaletteCanvas.ReleaseMouseCapture();

    private void PickAt(Point point)
    {
        var x = Math.Clamp(point.X, 0, CanvasWidth - 1);
        var y = Math.Clamp(point.Y, 0, CanvasHeight - 1);
        MoveMarker(x, y);

        var hue = x / (CanvasWidth - 1) * 360;
        var lightness = TopLightness - (TopLightness - BottomLightness) * y / (CanvasHeight - 1);
        var hex = ColorMath.ToHex(ColorMath.HslToColor(hue, Saturation, lightness));
        ShowColor(hex);
        changed(hex);
    }

    private void MoveMarker(double x, double y)
    {
        Canvas.SetLeft(Marker, Math.Clamp(x, 0, CanvasWidth - 1) - Marker.Width / 2);
        Canvas.SetTop(Marker, Math.Clamp(y, 0, CanvasHeight - 1) - Marker.Height / 2);
    }

    private void ShowColor(string hex)
    {
        Preview.Fill = new SolidColorBrush(ColorMath.ToColor(hex));
        HexText.Text = hex;
    }

    private static BitmapSource CreatePalette()
    {
        var pixels = new int[CanvasWidth * CanvasHeight];
        for (var y = 0; y < CanvasHeight; y++)
        {
            var lightness = TopLightness - (TopLightness - BottomLightness) * y / (CanvasHeight - 1);
            for (var x = 0; x < CanvasWidth; x++)
            {
                var color = ColorMath.HslToColor(x / (double)(CanvasWidth - 1) * 360, Saturation, lightness);
                pixels[y * CanvasWidth + x] = (255 << 24) | (color.R << 16) | (color.G << 8) | color.B;
            }
        }

        var bitmap = new WriteableBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, CanvasWidth, CanvasHeight), pixels, CanvasWidth * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
