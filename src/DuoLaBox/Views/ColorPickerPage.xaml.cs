using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DuoLaBox.Models;
using DuoLaBox.Services;
using MediaColor = System.Windows.Media.Color;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace DuoLaBox.Views;

public partial class ColorPickerPage : WpfUserControl
{
    private readonly ObservableCollection<ColorFormatItem> _formats = [];
    private readonly ObservableCollection<FavoriteColorItem> _favorites = [];
    private readonly ClipboardService _clipboardService = new();
    private MediaColor _currentColor =
        MediaColor.FromRgb(22, 137, 216);
    private bool _isPicking;

    public ColorPickerPage()
    {
        InitializeComponent();
        FormatList.ItemsSource = _formats;
        FavoriteList.ItemsSource = _favorites;
        UpdateCurrentColor(_currentColor);
    }

    public event Action<bool>? PickingChanged;

    public event Action<IReadOnlyList<string>>? FavoritesChanged;

    public void LoadFavorites(IEnumerable<string> colors)
    {
        _favorites.Clear();
        foreach (var hex in colors.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _favorites.Add(new FavoriteColorItem(hex));
            }
            catch
            {
                // 跳过旧配置中的无效颜色。
            }
        }

        UpdateFavoriteEmptyState();
    }

    public void SetPicking(bool active)
    {
        _isPicking = active;
        PickColorButton.Content = active ? "取消取色" : "开始取色";
    }

    public void SetPickedColor(MediaColor color)
    {
        UpdateCurrentColor(color);
        SetPicking(false);
    }

    private void PickColorButton_Click(object sender, RoutedEventArgs e)
    {
        SetPicking(!_isPicking);
        PickingChanged?.Invoke(_isPicking);
    }

    private void FavoriteCurrentButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var hex = ColorTextFormatter.ToHex(_currentColor);
        if (_favorites.Any(item =>
                string.Equals(
                    item.Hex,
                    hex,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _favorites.Insert(0, new FavoriteColorItem(hex));
        UpdateFavoriteEmptyState();
        RaiseFavoritesChanged();
    }

    private void RemoveFavoriteButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button
            {
                Tag: string hex
            })
        {
            return;
        }

        var item = _favorites.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Hex,
                hex,
                StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        _favorites.Remove(item);
        UpdateFavoriteEmptyState();
        RaiseFavoritesChanged();
    }

    private async void CopyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button
            {
                Tag: string value
            })
        {
            var copied =
                await _clipboardService.CopyTextAsync(value);
            OperationStatusText.Text = copied
                ? $"已复制：{value}"
                : "无法写入剪贴板，请稍后重试。";
        }
    }

    private void UpdateCurrentColor(MediaColor color)
    {
        _currentColor = color;
        CurrentColorSwatch.Background = new SolidColorBrush(color);
        CurrentHexText.Text = ColorTextFormatter.ToHex(color);

        _formats.Clear();
        foreach (var format in ColorTextFormatter.CreateFormats(color))
        {
            _formats.Add(format);
        }
    }

    private void RaiseFavoritesChanged()
    {
        FavoritesChanged?.Invoke(
            _favorites.Select(item => item.Hex).ToArray());
    }

    private void UpdateFavoriteEmptyState()
    {
        FavoriteEmptyText.Visibility = _favorites.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

}
