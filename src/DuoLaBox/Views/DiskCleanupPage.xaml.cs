using System.Windows;
using DuoLaBox.Models;
using Microsoft.Win32;

namespace DuoLaBox.Views;

public partial class DiskCleanupPage :
    System.Windows.Controls.UserControl
{
    private IReadOnlyList<CleanupCandidate> _candidates = [];
    private bool _isBusy;

    public DiskCleanupPage()
    {
        InitializeComponent();
    }

    public event Action? ScanRequested;

    public event Action<IReadOnlyList<CleanupCandidate>>? CleanRequested;

    public event Action<string>? SaveLibraryRequested;

    public event Action? RestoreLibraryRequested;

    public event Action<string>? ImportLibraryRequested;

    public void LoadLibrary(string content, string status)
    {
        LibraryTextBox.Text = content;
        LibraryStatusText.Text = status;
    }

    public void SetBusy(bool busy, string status)
    {
        _isBusy = busy;
        UpdateActionButtons();
        CleanupStatusTitleText.Text = busy ? "正在处理" : "清理状态";
        CleanupStatusText.Text = status;
    }

    public void SetScanResult(CleanupScanResult result)
    {
        _isBusy = false;
        _candidates = result.Candidates;
        ApplyCandidateSort();
        UpdateCandidateSummary();
        if (_candidates.Count == 0)
        {
            CleanupEmptyTitleText.Text = "没有发现可清理文件";
            CleanupEmptyDescriptionText.Text = "当前清理库扫描范围内没有可清理项目。";
        }
        InvalidRuleCountText.Text =
            result.InvalidRuleCount.ToString();

        var limitText = result.ReachedLimit
            ? " 已达到本次扫描的时间或数量上限，当前为部分结果。"
            : string.Empty;
        CleanupStatusTitleText.Text = "扫描完成";
        CleanupStatusText.Text =
            $"共读取 {result.TotalRuleCount} 条规则，" +
            $"{result.RecognizedRuleCount} 条已识别，" +
            $"{result.InvalidRuleCount} 条格式无效，" +
            $"{result.InaccessibleItemCount} 项无法访问。" +
            limitText;
    }

    public void SetExecutionResult(
        CleanupExecutionResult result,
        string? note = null)
    {
        _isBusy = false;
        var removedPaths = new HashSet<string>(
            result.DeletedPaths ?? [],
            StringComparer.OrdinalIgnoreCase);
        if (removedPaths.Count > 0)
        {
            _candidates = _candidates
                .Where(item => !removedPaths.Contains(item.Path))
                .ToArray();
            ApplyCandidateSort();
        }

        UpdateCandidateSummary();
        if (_candidates.Count == 0)
        {
            CleanupEmptyTitleText.Text = "所选文件已清理";
            CleanupEmptyDescriptionText.Text = "列表已即时移除成功删除的项目。";
        }
        CleanupStatusTitleText.Text = "清理完成";
        CleanupStatusText.Text = BuildExecutionStatus(result) +
            (string.IsNullOrWhiteSpace(note)
                ? string.Empty
                : $"{Environment.NewLine}{note}");
        CleanupConfirmBorder.Visibility = Visibility.Collapsed;
    }

    private static string BuildExecutionStatus(
        CleanupExecutionResult result)
    {
        var summary =
            $"已删除 {result.DeletedCount} 个文件 · " +
            $"释放 {CleanupSizeFormatter.Format(result.ReleasedBytes)}";
        if (result.FailedCount == 0)
        {
            return summary + Environment.NewLine +
                "所有选中项目均已处理。";
        }

        var failures = result.Failures ?? [];
        var inUse = failures.Count(item =>
            item.Kind == CleanupFailureKind.InUse);
        var accessDenied = failures.Count(item =>
            item.Kind == CleanupFailureKind.AccessDenied);
        var other = Math.Max(
            0,
            result.FailedCount - inUse - accessDenied);
        var reasons = new List<string>();
        if (inUse > 0)
        {
            reasons.Add($"{inUse} 个正在使用");
        }

        if (accessDenied > 0)
        {
            reasons.Add($"{accessDenied} 个受系统保护");
        }

        if (other > 0)
        {
            reasons.Add($"{other} 个暂时无法处理");
        }

        return summary + Environment.NewLine +
            $"保留 {result.FailedCount} 个项目：" +
            string.Join("、", reasons) + "。" + Environment.NewLine +
            "正在使用的文件可在关闭相关软件后重试；管理员清理后仍受保护的系统文件会安全保留，不会强行修改权限。";
    }

    private void UpdateCandidateSummary()
    {
        CandidateCountText.Text = _candidates.Count.ToString();
        CleanupSizeText.Text = CleanupSizeFormatter.Format(
            _candidates.Sum(item => item.Size));
        CleanupEmptyState.Visibility =
            _candidates.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        ScanButton.IsEnabled = !_isBusy;
        CleanSelectedButton.IsEnabled = !_isBusy && _candidates.Count > 0;
        CandidateList.IsEnabled = !_isBusy;
    }

    public void SetLibraryStatus(string status)
    {
        LibraryStatusText.Text = status;
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanRequested?.Invoke();
    }

    private void CleanupSortComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplyCandidateSort();
    }

    private void ApplyCandidateSort()
    {
        if (CandidateList is null)
        {
            return;
        }

        _candidates = CleanupCandidateSorter.Sort(
            _candidates,
            CleanupSortComboBox.SelectedIndex == 1
                ? CleanupSortMode.FileSize
                : CleanupSortMode.Name);
        CandidateList.ItemsSource = _candidates;
    }

    private void CleanSelectedButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selected = _candidates
            .Where(item => item.IsSelected)
            .ToArray();
        if (selected.Length == 0)
        {
            CleanupStatusTitleText.Text = "无法开始清理";
            CleanupStatusText.Text = "请至少选择一个要清理的文件。";
            return;
        }

        CleanupConfirmText.Text =
            $"将删除 {selected.Length} 个文件，预计释放 " +
            $"{CleanupSizeFormatter.Format(selected.Sum(item => item.Size))}。";
        CleanupConfirmBorder.Visibility = Visibility.Visible;
    }

    private void ConfirmCleanupButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var selected = _candidates
            .Where(item => item.IsSelected)
            .ToArray();
        CleanupConfirmBorder.Visibility = Visibility.Collapsed;
        CleanRequested?.Invoke(selected);
    }

    private void CancelCleanupButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CleanupConfirmBorder.Visibility = Visibility.Collapsed;
    }

    private void EditLibraryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CleanupPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
    }

    private void BackToCleanupButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LibraryPanel.Visibility = Visibility.Collapsed;
        CleanupPanel.Visibility = Visibility.Visible;
    }

    private void SaveLibraryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveLibraryRequested?.Invoke(LibraryTextBox.Text);
    }

    private void RestoreLibraryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        RestoreLibraryRequested?.Invoke();
    }

    private void ImportLibraryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入清理规则",
            Filter = "清理规则 (*.rules;*.txt)|*.rules;*.txt|所有文件 (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            ImportLibraryRequested?.Invoke(dialog.FileName);
        }
    }
}
