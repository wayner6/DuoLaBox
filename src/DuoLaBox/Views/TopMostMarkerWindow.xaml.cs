using System.Windows;
using System.Windows.Interop;
using DuoLaBox.Services;

namespace DuoLaBox.Views;

public partial class TopMostMarkerWindow : Window
{
    public TopMostMarkerWindow(nint targetHandle)
    {
        TargetHandle = targetHandle;
        InitializeComponent();
    }

    public event Action<nint>? UnpinRequested;

    public nint TargetHandle { get; }

    public nint MarkerHandle => new WindowInteropHelper(this).Handle;

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var extendedStyle = NativeMethods.GetWindowLongPtr(
            MarkerHandle,
            NativeMethods.GwlExStyle).ToInt64();

        extendedStyle |= NativeMethods.WsExToolWindow |
                         NativeMethods.WsExNoActivate;

        NativeMethods.SetWindowLongPtr(
            MarkerHandle,
            NativeMethods.GwlExStyle,
            new nint(extendedStyle));
    }

    private void MarkerButton_Click(object sender, RoutedEventArgs e)
    {
        UnpinRequested?.Invoke(TargetHandle);
    }
}
