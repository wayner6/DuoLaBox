using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuoLaBox.Models;
using DuoLaBox.Services;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfControls = System.Windows.Controls;
using WpfPrimitives = System.Windows.Controls.Primitives;

namespace DuoLaBox.Views;

public partial class FileBatchPage : WpfControls.UserControl
{
    private readonly ObservableCollection<string> _renameFiles = [];
    private readonly ObservableCollection<string> _unlockFiles = [];
    private readonly ObservableCollection<string> _resizeFiles = [];
    private readonly ObservableCollection<RenamePreviewItem> _renamePreview = [];
    private readonly ObservableCollection<LockingProcessInfo> _lockingProcesses = [];
    private readonly BatchRenameService _renameService = new();
    private readonly FileLockService _fileLockService = new();
    private readonly ImageResizeService _imageResizeService = new();

    private FileToolMode _currentMode = FileToolMode.Rename;
    private bool _initialized;
    private bool _renameContextMenuEnabled;
    private bool _unlockContextMenuEnabled;
    private bool _resizeContextMenuEnabled;
    private TaskCompletionSource<bool>? _confirmCompletion;
    private readonly Queue<(FileToolMode Mode, string[] Paths)> _pendingOpenRequests = new();
    private bool _isBusy;

    internal bool IsBusy => _isBusy;
    public bool AreContextMenusBusy { get; private set; }

    public void SetContextMenusBusy(bool busy)
    {
        AreContextMenusBusy = busy;
        RenameContextMenuButton.IsEnabled = !busy;
        UnlockContextMenuButton.IsEnabled = !busy;
        ResizeContextMenuButton.IsEnabled = !busy;
    }

    public FileBatchPage()
    {
        InitializeComponent();
        RenamePreviewList.ItemsSource = _renamePreview;
        LockingProcessesList.ItemsSource = _lockingProcesses;
        SelectedFilesList.ItemsSource = _renameFiles;
        _initialized = true;
        RefreshRenamePreview();
    }

    public event Action<FileToolMode, bool>? ContextMenuSettingChanged;

    private ObservableCollection<string> CurrentFiles =>
        _currentMode switch
        {
            FileToolMode.Rename => _renameFiles,
            FileToolMode.Unlock => _unlockFiles,
            FileToolMode.ResizeImages => _resizeFiles,
            _ => _renameFiles
        };

    public void Open(FileToolMode mode, IEnumerable<string> paths)
    {
        if (_isBusy)
        {
            // ponytail: at most 64 pending requests; coalesce by mode if larger shell batches are needed.
            if (_pendingOpenRequests.Count >= 64)
            {
                StatusText.Text = "待添加文件的请求队列已满，请完成当前操作后重新添加。";
                return;
            }
            _pendingOpenRequests.Enqueue((mode, paths.ToArray()));
            StatusText.Text = "当前操作尚未完成，收到的文件已排队，完成或取消后自动添加。";
            return;
        }

        ShowMode(mode);
        AddFiles(paths);
        if (mode == FileToolMode.Unlock && CurrentFiles.Count > 0)
        {
            _ = AnalyzeLocksAsync();
        }
    }

    public void LoadContextMenuSettings(AppSettings settings)
    {
        _renameContextMenuEnabled =
            settings.RenameContextMenuEnabled;
        _unlockContextMenuEnabled =
            settings.UnlockContextMenuEnabled;
        _resizeContextMenuEnabled =
            settings.ResizeContextMenuEnabled;
        UpdateContextMenuButtons();
    }

    public void SetContextMenuSetting(
        FileToolMode mode,
        bool enabled)
    {
        switch (mode)
        {
            case FileToolMode.Rename:
                _renameContextMenuEnabled = enabled;
                break;
            case FileToolMode.Unlock:
                _unlockContextMenuEnabled = enabled;
                break;
            case FileToolMode.ResizeImages:
                _resizeContextMenuEnabled = enabled;
                break;
        }

        UpdateContextMenuButtons();
    }

    private void ShowMode(FileToolMode mode)
    {
        _currentMode = mode;
        RenamePanel.Visibility = mode == FileToolMode.Rename
            ? Visibility.Visible
            : Visibility.Collapsed;
        UnlockPanel.Visibility = mode == FileToolMode.Unlock
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResizePanel.Visibility = mode == FileToolMode.ResizeImages
            ? Visibility.Visible
            : Visibility.Collapsed;

        RenameModeButton.Style = GetModeButtonStyle(
            mode == FileToolMode.Rename);
        UnlockModeButton.Style = GetModeButtonStyle(
            mode == FileToolMode.Unlock);
        ResizeModeButton.Style = GetModeButtonStyle(
            mode == FileToolMode.ResizeImages);

        SelectedFilesList.ItemsSource = CurrentFiles;
        UpdateSelectedCount();
        if (mode == FileToolMode.Rename)
        {
            RefreshRenamePreview();
        }
        else if (mode == FileToolMode.ResizeImages)
        {
            SelectFirstResizeImage();
        }
    }

    private Style GetModeButtonStyle(bool selected) =>
        (Style)FindResource(
            selected
                ? "SkyBlueButtonStyle"
                : "SecondaryButtonStyle");

    private void AddFiles(IEnumerable<string> paths)
    {
        var candidates = paths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var skipped = 0;
        string? firstAddedPath = null;
        foreach (var path in candidates)
        {
            if (_currentMode == FileToolMode.ResizeImages &&
                !ImageResizeService.IsSupported(path))
            {
                skipped++;
                continue;
            }

            if (!CurrentFiles.Contains(
                    path,
                    StringComparer.OrdinalIgnoreCase))
            {
                CurrentFiles.Add(path);
                firstAddedPath ??= path;
            }
        }

        UpdateSelectedCount();
        if (_currentMode == FileToolMode.Rename)
        {
            RefreshRenamePreview();
        }
        else if (_currentMode == FileToolMode.Unlock)
        {
            _lockingProcesses.Clear();
        }
        else
        {
            if (firstAddedPath is not null)
            {
                SelectedFilesList.SelectedItem = firstAddedPath;
            }
            else
            {
                SelectFirstResizeImage();
            }
        }

        StatusText.Text = skipped > 0
            ? $"已添加 {candidates.Length - skipped} 张图片，跳过 {skipped} 个不支持的文件。"
            : CurrentFiles.Count == 0
                ? "请先添加文件。"
                : $"当前功能已选择 {CurrentFiles.Count} 个文件。";
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var imageMode = _currentMode == FileToolMode.ResizeImages;
        var dialog = new OpenFileDialog
        {
            Title = imageMode ? "选择要调整的图片" : "选择要处理的文件",
            Multiselect = true,
            CheckFileExists = true,
            Filter = imageMode
                ? "支持的图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*"
                : "所有文件|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private void RemoveFileButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not WpfControls.Button { Tag: string path })
        {
            return;
        }

        CurrentFiles.Remove(path);
        if (_currentMode == FileToolMode.Rename)
        {
            RefreshRenamePreview();
        }
        else if (_currentMode == FileToolMode.Unlock)
        {
            _lockingProcesses.Clear();
        }
        else
        {
            SelectFirstResizeImage();
        }

        UpdateSelectedCount();
        StatusText.Text = CurrentFiles.Count == 0
            ? "请先添加文件。"
            : "已从当前功能的列表中移除文件。";
    }

    private void ClearFilesButton_Click(object sender, RoutedEventArgs e)
    {
        CurrentFiles.Clear();
        if (_currentMode == FileToolMode.Rename)
        {
            _renamePreview.Clear();
        }
        else if (_currentMode == FileToolMode.Unlock)
        {
            _lockingProcesses.Clear();
        }
        else
        {
            ClearImagePreview();
        }

        UpdateSelectedCount();
        StatusText.Text = "已清空当前功能的文件列表。";
    }

    private void RenameModeButton_Click(object sender, RoutedEventArgs e) =>
        ShowMode(FileToolMode.Rename);

    private void UnlockModeButton_Click(object sender, RoutedEventArgs e) =>
        ShowMode(FileToolMode.Unlock);

    private void ResizeModeButton_Click(object sender, RoutedEventArgs e) =>
        ShowMode(FileToolMode.ResizeImages);

    private void RenameContextMenuButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ToggleContextMenu(
            FileToolMode.Rename,
            RenameContextMenuButton.IsChecked == true);
    }

    private void UnlockContextMenuButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ToggleContextMenu(
            FileToolMode.Unlock,
            UnlockContextMenuButton.IsChecked == true);
    }

    private void ResizeContextMenuButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ToggleContextMenu(
            FileToolMode.ResizeImages,
            ResizeContextMenuButton.IsChecked == true);
    }

    private void ToggleContextMenu(FileToolMode mode, bool enabled)
    {
        if (AreContextMenusBusy)
        {
            UpdateContextMenuButtons();
            return;
        }
        SetContextMenuSetting(mode, enabled);
        ContextMenuSettingChanged?.Invoke(mode, enabled);
    }

    private void UpdateContextMenuButtons()
    {
        SetContextMenuToggle(
            RenameContextMenuButton,
            "批量重命名",
            _renameContextMenuEnabled);
        SetContextMenuToggle(
            UnlockContextMenuButton,
            "解除文件占用",
            _unlockContextMenuEnabled);
        SetContextMenuToggle(
            ResizeContextMenuButton,
            "调整图片尺寸",
            _resizeContextMenuEnabled);
    }

    private static void SetContextMenuToggle(
        WpfPrimitives.ToggleButton button,
        string featureName,
        bool enabled)
    {
        button.Content = $"添加{featureName}到鼠标右键菜单";
        button.IsChecked = enabled;
    }

    internal async Task RunBusyAsync(Func<Task> action)
    {
        if (_isBusy)
        {
            return;
        }
        _isBusy = true;
        ModeButtonsPanel.IsEnabled = false;
        FileSelectionPanel.IsEnabled = false;
        FileOperationsPanel.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            _isBusy = false;
            ModeButtonsPanel.IsEnabled = true;
            FileSelectionPanel.IsEnabled = true;
            FileOperationsPanel.IsEnabled = true;
            while (!_isBusy && _pendingOpenRequests.TryDequeue(out var request))
            {
                Open(request.Mode, request.Paths);
            }
        }
    }

    private void UpdateSelectedCount()
    {
        var featureName = _currentMode switch
        {
            FileToolMode.Rename => "批量重命名",
            FileToolMode.Unlock => "解除文件占用",
            FileToolMode.ResizeImages => "调整图片尺寸",
            _ => "文件处理"
        };
        SelectedCountText.Text =
            $"{featureName} · 已选择 {CurrentFiles.Count} 个文件";
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(
            Window.GetWindow(this),
            message,
            AppIdentity.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
