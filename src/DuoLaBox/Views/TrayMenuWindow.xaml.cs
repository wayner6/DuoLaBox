using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using DuoLaBox.Services;
using Forms = System.Windows.Forms;

namespace DuoLaBox.Views;

public partial class TrayMenuWindow : Window
{
    private bool _allowClose;

    public TrayMenuWindow()
    {
        InitializeComponent();
    }

    public event Action? ShowRequested;

    public event Action? MousePickRequested;

    public event Action? ExitRequested;

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    public void ShowAtCursor()
    {
        if (IsVisible)
        {
            Hide();
        }

        var pointer = Forms.Cursor.Position;
        var workingArea = Forms.Screen.FromPoint(pointer).WorkingArea;

        Show();
        var handle = new WindowInteropHelper(this).Handle;

        // 各显示器缩放可能不同：先把窗口移到指针所在的显示器，
        // 让它按那台显示器的缩放重新布局，再用实际像素计算最终位置。
        MoveTo(handle, pointer.X, pointer.Y);
        UpdateLayout();
        var dpi = NativeMethods.GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var (x, y) = CalculatePosition(
            pointer,
            workingArea,
            ActualWidth * scale,
            ActualHeight * scale,
            8 * scale);
        MoveTo(handle, x, y);

        Activate();
    }

    internal static (int X, int Y) CalculatePosition(
        System.Drawing.Point pointer,
        Rectangle workingArea,
        double width,
        double height,
        double gap)
    {
        var x = pointer.X + width + gap <= workingArea.Right
            ? pointer.X + gap
            : Math.Max(workingArea.Left + gap, pointer.X - width - gap);
        var y = pointer.Y + height + gap <= workingArea.Bottom
            ? pointer.Y + gap
            : Math.Max(workingArea.Top + gap, pointer.Y - height - gap);
        return ((int)Math.Round(x), (int)Math.Round(y));
    }

    private static void MoveTo(nint handle, int x, int y)
    {
        NativeMethods.SetWindowPos(
            handle,
            nint.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpNoZOrder);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Hide();
    }

    private void Window_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void ShowButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ShowRequested?.Invoke();
    }

    private void MousePickButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        MousePickRequested?.Invoke();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ExitRequested?.Invoke();
    }
}
