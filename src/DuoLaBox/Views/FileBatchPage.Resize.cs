using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuoLaBox.Models;
using DuoLaBox.Services;
using MessageBox = System.Windows.MessageBox;
using WpfControls = System.Windows.Controls;

namespace DuoLaBox.Views;

public partial class FileBatchPage
{
    private void SelectedFilesList_SelectionChanged(
        object sender,
        WpfControls.SelectionChangedEventArgs e)
    {
        if (_initialized &&
            _currentMode == FileToolMode.ResizeImages)
        {
            ApplySelectedImageOriginalSize();
            UpdateImagePreview();
        }
    }

    private void ApplySelectedImageOriginalSize()
    {
        if (SelectedFilesList.SelectedItem is not string path ||
            !File.Exists(path))
        {
            return;
        }

        try
        {
            var image = LoadImage(path);
            ResizeWidthTextBox.Text = image.PixelWidth.ToString();
            ResizeHeightTextBox.Text = image.PixelHeight.ToString();
        }
        catch
        {
            // 预览方法会显示具体的读取错误。
        }
    }

    private void SelectFirstResizeImage()
    {
        if (_currentMode != FileToolMode.ResizeImages)
        {
            return;
        }

        if (_resizeFiles.Count == 0)
        {
            ClearImagePreview();
            return;
        }

        if (SelectedFilesList.SelectedItem is not string selected ||
            !_resizeFiles.Contains(selected))
        {
            SelectedFilesList.SelectedIndex = 0;
        }

        UpdateImagePreview();
    }

    private void ResizeInput_TextChanged(
        object sender,
        WpfControls.TextChangedEventArgs e)
    {
        if (_initialized)
        {
            UpdateImagePreview();
        }
    }

    private void PreserveRatioToggle_Click(
        object sender,
        RoutedEventArgs e)
    {
        PreserveRatioToggle.Content =
            PreserveRatioToggle.IsChecked == true
                ? "保持比例"
                : "拉伸填充";
        UpdateImagePreview();
    }

    private void UpdateImagePreview()
    {
        if (SelectedFilesList.SelectedItem is not string path ||
            !File.Exists(path) ||
            !ImageResizeService.IsSupported(path))
        {
            ClearImagePreview();
            return;
        }

        try
        {
            var image = LoadImage(path);
            OriginalPreviewImage.Source = image;
            TargetPreviewImage.Source = image;
            OriginalImageInfoText.Text =
                $"原图 · {image.PixelWidth} × {image.PixelHeight}";

            if (!int.TryParse(ResizeWidthTextBox.Text, out var width) ||
                !int.TryParse(ResizeHeightTextBox.Text, out var height) ||
                width < 1 ||
                height < 1)
            {
                TargetImageInfoText.Text = "修改后预览 · 请输入有效尺寸";
                TargetPreviewCanvas.Width = 220;
                TargetPreviewCanvas.Height = 130;
                return;
            }

            TargetImageInfoText.Text = $"修改后 · {width} × {height}";
            var scale = Math.Min(250d / width, 140d / height);
            TargetPreviewCanvas.Width = Math.Max(20, width * scale);
            TargetPreviewCanvas.Height = Math.Max(20, height * scale);
            TargetPreviewImage.Stretch =
                PreserveRatioToggle.IsChecked == true
                    ? Stretch.Uniform
                    : Stretch.Fill;
        }
        catch (Exception exception)
        {
            ClearImagePreview();
            StatusText.Text = $"无法预览图片：{exception.Message}";
        }
    }

    private static BitmapSource LoadImage(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private void ClearImagePreview()
    {
        OriginalPreviewImage.Source = null;
        TargetPreviewImage.Source = null;
        OriginalImageInfoText.Text = "原图预览";
        TargetImageInfoText.Text = "修改后预览";
        TargetPreviewCanvas.Width = 220;
        TargetPreviewCanvas.Height = 130;
    }

    private async void ExecuteResizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            if (!int.TryParse(ResizeWidthTextBox.Text, out var width) ||
                !int.TryParse(ResizeHeightTextBox.Text, out var height))
            {
                ShowError("宽度和高度必须是整数。");
                return;
            }

            var files = _resizeFiles.ToArray();
            var preserveAspectRatio = PreserveRatioToggle.IsChecked == true;
            if (files.Length == 0)
            {
                ShowError("请选择至少一张受支持的图片。");
                return;
            }

            if (MessageBox.Show(
                    Window.GetWindow(this),
                    $"将调整 {files.Length} 张图片为 {width} × {height}，" +
                    "结果保存到新文件夹，不修改原图。是否继续？",
                    "确认调整图片尺寸",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            var outputs = await _imageResizeService.ResizeAsync(
                files, width, height, preserveAspectRatio);
            StatusText.Text =
                $"已处理 {outputs.Count} 张图片，原图保持不变。";
        });
    }

}
