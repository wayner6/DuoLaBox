using System.Windows;
using DuoLaBox.Services;
using MediaColor = System.Windows.Media.Color;

namespace DuoLaBox;

public partial class MainWindow
{
    private void ColorPickerPage_PickingChanged(bool active)
    {
        if (!active)
        {
            _screenColorPicker.Cancel();
            _colorPreviewWindow.HidePreview();
            return;
        }

        if (!_screenColorPicker.Start())
        {
            ColorPickerPage.SetPicking(false);
            System.Windows.MessageBox.Show(
                this,
                "无法启动屏幕取色，请重试。",
                AppIdentity.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _refreshTimer.Stop();
        Hide();
    }

    private void ScreenColorPicker_ColorPicked(MediaColor color)
    {
        _colorPreviewWindow.HidePreview();
        ShowFromTray();
        ShowPage(ColorPickerPage, ColorPickerNavButton);
        ColorPickerPage.SetPickedColor(color);
    }

    private void ScreenColorPicker_PickingFailed()
    {
        _colorPreviewWindow.HidePreview();
        ShowFromTray();
        ShowPage(ColorPickerPage, ColorPickerNavButton);
        ColorPickerPage.SetPicking(false);
        System.Windows.MessageBox.Show(
            this,
            "未能读取该位置的屏幕颜色，请重试。",
            AppIdentity.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ScreenColorPicker_PreviewChanged(
        ScreenColorSample sample)
    {
        _colorPreviewWindow.ShowSample(sample);
    }

    private void ColorPickerPage_FavoritesChanged(
        IReadOnlyList<string> favorites)
    {
        _settings.FavoriteColors = [.. favorites];
        SaveSettings();
    }
}
