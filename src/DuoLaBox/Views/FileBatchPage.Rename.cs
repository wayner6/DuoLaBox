using System.Windows;
using DuoLaBox.Models;
using WpfControls = System.Windows.Controls;

namespace DuoLaBox.Views;

public partial class FileBatchPage
{
    private void RenameInput_TextChanged(
        object sender,
        WpfControls.TextChangedEventArgs e)
    {
        if (_initialized)
        {
            RefreshRenamePreview();
        }
    }

    private bool RefreshRenamePreview(bool showError = false)
    {
        _renamePreview.Clear();
        if (_renameFiles.Count == 0)
        {
            if (_currentMode == FileToolMode.Rename)
            {
                StatusText.Text = "请先添加需要重命名的文件。";
            }

            return false;
        }

        try
        {
            if (!int.TryParse(
                    RenameStartIndexTextBox.Text,
                    out var startIndex))
            {
                throw new InvalidOperationException("起始序号必须是整数。");
            }

            var plan = _renameService.CreatePlan(
                _renameFiles,
                RenameTemplateTextBox.Text,
                startIndex);
            foreach (var item in plan)
            {
                _renamePreview.Add(item);
            }

            if (_currentMode == FileToolMode.Rename)
            {
                StatusText.Text =
                    $"预览已自动更新，共 {plan.Count} 个文件。";
            }

            return plan.Count > 0;
        }
        catch (Exception exception)
        {
            if (_currentMode == FileToolMode.Rename)
            {
                StatusText.Text = exception.Message;
            }

            if (showError)
            {
                ShowError(exception.Message);
            }

            return false;
        }
    }

    private async void ExecuteRenameButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            if (!RefreshRenamePreview(showError: true))
            {
                return;
            }

            var plan = _renamePreview.ToArray();
            if (!await ShowRenameConfirmationAsync(plan.Length))
            {
                return;
            }

            await _renameService.ExecuteAsync(plan);
            _renameFiles.Clear();
            foreach (var item in plan)
            {
                _renameFiles.Add(item.DestinationPath);
            }

            UpdateSelectedCount();
            RefreshRenamePreview();
            StatusText.Text = $"已完成 {plan.Length} 个文件的重命名。";
        });
    }


    private Task<bool> ShowRenameConfirmationAsync(int count)
    {
        _confirmCompletion?.TrySetResult(false);
        _confirmCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ModernConfirmMessage.Text =
            $"即将按照当前预览重命名 {count} 个文件。" +
            "重命名过程中如发生错误，软件会尽力恢复原文件名。";
        ModernConfirmOverlay.Visibility = Visibility.Visible;
        return _confirmCompletion.Task;
    }

    private void ModernConfirmCancelButton_Click(
        object sender,
        RoutedEventArgs e) =>
        CompleteModernConfirmation(false);

    private void ModernConfirmAcceptButton_Click(
        object sender,
        RoutedEventArgs e) =>
        CompleteModernConfirmation(true);

    private void CompleteModernConfirmation(bool accepted)
    {
        ModernConfirmOverlay.Visibility = Visibility.Collapsed;
        var completion = _confirmCompletion;
        _confirmCompletion = null;
        completion?.TrySetResult(accepted);
    }

}
