using System.ComponentModel;
using System.Drawing;
using System.Windows;
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
        using var graphics = Graphics.FromHwnd(nint.Zero);
        var scaleX = graphics.DpiX / 96d;
        var scaleY = graphics.DpiY / 96d;

        Show();
        UpdateLayout();

        var pointerX = pointer.X / scaleX;
        var pointerY = pointer.Y / scaleY;
        var workLeft = workingArea.Left / scaleX;
        var workTop = workingArea.Top / scaleY;
        var workRight = workingArea.Right / scaleX;
        var workBottom = workingArea.Bottom / scaleY;
        const double gap = 8;

        Left = pointerX + ActualWidth + gap <= workRight
            ? pointerX + gap
            : Math.Max(workLeft + gap, pointerX - ActualWidth - gap);
        Top = pointerY + ActualHeight + gap <= workBottom
            ? pointerY + gap
            : Math.Max(workTop + gap, pointerY - ActualHeight - gap);

        Activate();
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
