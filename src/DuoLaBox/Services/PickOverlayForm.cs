using System.Drawing;
using Forms = System.Windows.Forms;

namespace DuoLaBox.Services;

// 选窗时铺满所有显示器的近乎透明的窗口。
// 指针样式只属于这个窗口，程序异常退出时不会留在系统里；
// 点击也由它接收，不会落到目标窗口的关闭按钮或标签页上。
internal sealed class PickOverlayForm : Forms.Form
{
    private const int WmDpiChanged = 0x02E0;
    private const int WsExToolWindow = 0x00000080;

    public PickOverlayForm(Forms.Cursor cursor)
    {
        AutoScaleMode = Forms.AutoScaleMode.None;
        FormBorderStyle = Forms.FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = Forms.FormStartPosition.Manual;
        Bounds = Forms.SystemInformation.VirtualScreen;
        BackColor = Color.Black;
        // 完全透明的窗口收不到鼠标消息；保留肉眼不可见的最低不透明度。
        Opacity = 0.01;
        Cursor = cursor;
        KeyPreview = true;
    }

    public event Action<Point>? PointClicked;

    public event Action? CancelRequested;

    protected override Forms.CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow;
            return parameters;
        }
    }

    protected override void OnMouseUp(Forms.MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == Forms.MouseButtons.Left)
        {
            PointClicked?.Invoke(PointToScreen(e.Location));
        }
        else if (e.Button == Forms.MouseButtons.Right)
        {
            CancelRequested?.Invoke();
        }
    }

    protected override void OnKeyDown(Forms.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Forms.Keys.Escape)
        {
            e.Handled = true;
            CancelRequested?.Invoke();
        }
    }

    protected override void WndProc(ref Forms.Message m)
    {
        // 窗口横跨多个显示器；不能因为某一台的缩放而被系统改变尺寸。
        if (m.Msg == WmDpiChanged)
        {
            return;
        }

        base.WndProc(ref m);
    }
}
