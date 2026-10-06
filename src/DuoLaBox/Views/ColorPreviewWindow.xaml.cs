using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DuoLaBox.Services;
using Forms = System.Windows.Forms;

namespace DuoLaBox.Views;

public partial class ColorPreviewWindow : Window
{
    public ColorPreviewWindow()
    {
        InitializeComponent();
    }

    public void ShowSample(ScreenColorSample sample)
    {
        PreviewSwatch.Background =
            new SolidColorBrush(sample.Color);
        HexText.Text =
            $"#{sample.Color.R:X2}{sample.Color.G:X2}{sample.Color.B:X2}";
        RgbText.Text =
            $"RGB {sample.Color.R}, {sample.Color.G}, {sample.Color.B}";

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionNearPointer(sample.ScreenX, sample.ScreenY);
    }

    public void HidePreview()
    {
        Hide();
    }

    private void PositionNearPointer(int screenX, int screenY)
    {
        var pointer = new System.Drawing.Point(screenX, screenY);
        var workingArea = Forms.Screen.FromPoint(pointer).WorkingArea;
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = handle == nint.Zero
            ? 96u
            : NativeMethods.GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var width = ActualWidth * scale;
        var height = ActualHeight * scale;
        const int gap = 22;

        var left = screenX + gap;
        if (left + width > workingArea.Right)
        {
            left = (int)(screenX - width - gap);
        }

        var top = screenY + gap;
        if (top + height > workingArea.Bottom)
        {
            top = (int)(screenY - height - gap);
        }

        Left = left / scale;
        Top = top / scale;
    }

    private void Window_SourceInitialized(
        object? sender,
        EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow |
                 NativeMethods.WsExNoActivate;
        style &= ~NativeMethods.WsExAppWindow;
        NativeMethods.SetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle,
            new nint(style));
    }
}
