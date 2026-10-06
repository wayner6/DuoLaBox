using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DuoLaBox.Models;

namespace DuoLaBox;

public partial class MainWindow
{
    private void MousePickButton_Click(object sender, RoutedEventArgs e)
    {
        if (_windowPicker.IsActive)
        {
            CancelMousePicking();
            return;
        }

        BeginMousePicking();
    }

    private void Window_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_windowPicker.IsActive)
        {
            return;
        }

        CancelMousePicking();
        e.Handled = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        _windowView?.Refresh();
        if (WindowList is not null)
        {
            UpdateResultState();
        }
    }

    private void TopMostButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton
            {
                Tag: WindowInfo window
            } toggle)
        {
            return;
        }

        var requestedState = toggle.IsChecked == true;
        if (!_topMostCoordinator.SetTopMost(window.Handle, requestedState))
        {
            toggle.IsChecked = window.IsTopMost;
            ShowTopMostOperationError(requestedState);
            return;
        }

        PickerStatusText.Text = requestedState
            ? $"已置顶：{window.Title}"
            : $"已取消置顶：{window.Title}";
        RefreshWindows(force: true);
    }

    private void WindowPicker_WindowPicked(nint handle)
    {
        var window = _topMostCoordinator
            .GetOpenWindows()
            .FirstOrDefault(candidate => candidate.Handle == handle);

        if (!_topMostCoordinator.SetTopMost(handle, topMost: true))
        {
            PickerStatusText.Text = "置顶失败：目标窗口可能已关闭或需要管理员权限。";
            ResetMousePickerButton();
            return;
        }

        var title = window?.Title ?? "目标窗口";
        PickerStatusText.Text = $"已置顶：{title}";
        ResetMousePickerButton();
        RefreshWindows(force: true);
    }

    private void WindowPicker_InvalidWindowTopClick()
    {
        PickerStatusText.Text = "没有命中窗口顶部，请再点击一次目标窗口的顶部区域。";
    }

    private void TopMostCoordinator_MarkerUnpinned(nint handle)
    {
        PickerStatusText.Text = "已取消窗口置顶。";
        RefreshWindows(force: true);
    }

    private void TopMostCoordinator_MarkerUnpinFailed(nint handle)
    {
        ShowTopMostOperationError(topMost: false);
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible ||
            WindowState == WindowState.Minimized ||
            WindowToolsPage.Visibility != Visibility.Visible ||
            _windowPicker.IsActive)
        {
            return;
        }

        RefreshWindows();
    }

    private void RefreshWindows(bool force = false)
    {
        var windows = _topMostCoordinator.GetOpenWindows();
        var snapshot = string.Join(
            '\n',
            windows.Select(window =>
                $"{window.Handle}:{window.IsTopMost}:{window.Title}:{window.ProcessName}:{window.Icon is not null}"));

        if (!force && snapshot == _windowSnapshot)
        {
            return;
        }

        _windowSnapshot = snapshot;
        _windowView = CollectionViewSource.GetDefaultView(windows);
        _windowView.Filter = MatchesSearch;
        WindowList.ItemsSource = _windowView;
        UpdateResultState();
    }

    private bool MatchesSearch(object item)
    {
        if (item is not WindowInfo window)
        {
            return false;
        }

        var query = SearchBox.Text.Trim();
        return query.Length == 0 ||
               window.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               window.ProcessName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateResultState()
    {
        var count = _windowView?.Cast<object>().Count() ?? 0;
        WindowCountText.Text = $"共 {count} 个窗口";
        EmptyState.Visibility = count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        WindowList.Visibility = count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BeginMousePicking()
    {
        _windowPicker.Start();
        MousePickButtonText.Text = "取消选择";
        PickerStatusText.Text = "请点击目标窗口的顶部区域；按 Esc 可取消。";

    }

    private void CancelMousePicking()
    {
        _windowPicker.Cancel();
        PickerStatusText.Text = "已取消选择。点击按钮可重新开始。";
        ResetMousePickerButton();
    }

    private void ResetMousePickerButton()
    {
        MousePickButtonText.Text = "开始选择";
    }

    private void ShowTopMostOperationError(bool topMost)
    {
        System.Windows.MessageBox.Show(
            this,
            topMost
                ? "置顶失败。目标窗口可能已经关闭，或需要管理员权限。"
                : "取消置顶失败。目标窗口可能已经关闭，或需要管理员权限。",
            AppIdentity.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
