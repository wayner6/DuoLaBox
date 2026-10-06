using System.IO;
using System.Windows;
using System.Windows.Controls;
using DuoLaBox.Models;
using DuoLaBox.Services;
using DuoLaBox.Views;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Use a plain WPF Application: DuoLaBox.App startup changes system integrations.
        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        application.Startup += async (_, _) =>
        {
            try
            {
                foreach (var name in new[] { "ThemeResources", "ScrollBarStyles", "ControlStyles" })
                {
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"pack://application:,,,/DuoLaBox;component/Resources/{name}.xaml")
                    });
                }

                VerifyCleanupPage();
                VerifyHostsEditor();
                await VerifyRenameAsync();
                await StorageAndScanChecks.RunAsync();
                await ConcurrencyChecks.RunAsync();
                Console.WriteLine("REGRESSION_VERIFICATION_OK");
                application.Shutdown(0);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                application.Shutdown(1);
            }
        };
        return application.Run();
    }

    private static void VerifyCleanupPage()
    {
        var page = new DiskCleanupPage();
        var scanButton = (Button)page.FindName("ScanButton");
        var cleanButton = (Button)page.FindName("CleanSelectedButton");
        var list = (ListView)page.FindName("CandidateList");
        var sort = (ComboBox)page.FindName("CleanupSortComboBox");
        var first = new CleanupCandidate { Path = @"C:\Temp\first.tmp", Rule = @"C:\Temp\*.tmp", Size = 10 };
        var second = new CleanupCandidate { Path = @"C:\Temp\second.tmp", Rule = @"C:\Temp\*.tmp", Size = 20 };

        page.SetScanResult(new CleanupScanResult([first, second], 1, 1, 0, 0, false));
        Assert(list.Items.Count == 2 && cleanButton.IsEnabled, "Scan result not displayed.");
        page.SetBusy(true, "Cleaning");
        sort.SelectedIndex = 1;
        Assert(!scanButton.IsEnabled && !cleanButton.IsEnabled && !list.IsEnabled,
            "Busy controls were re-enabled by sorting.");
        page.SetExecutionResult(new CleanupExecutionResult(1, 1, 10, [first.Path], [second.Path]));
        Assert(scanButton.IsEnabled && cleanButton.IsEnabled && list.Items.Count == 1,
            "Partial cleanup did not restore controls or remove deleted items.");
        page.SetBusy(true, "Cleaning");
        page.SetExecutionResult(new CleanupExecutionResult(1, 0, 20, [second.Path]));
        Assert(scanButton.IsEnabled && !cleanButton.IsEnabled && list.Items.Count == 0,
            "Full cleanup left stale items or a disabled scan button.");

        page.SetScanResult(new CleanupScanResult([first], 1, 1, 0, 0, false));
        page.SetScanResult(new CleanupScanResult([], 1, 1, 0, 0, false));
        Assert(list.Items.Count == 0 && !cleanButton.IsEnabled, "Empty scan retained old items.");
        page.SetBusy(true, "Scanning");
        page.SetBusy(false, "Scan failed");
        Assert(scanButton.IsEnabled && list.IsEnabled, "Failure did not restore controls.");
    }

    private static void VerifyHostsEditor()
    {
        var page = new SystemToolsPage();
        var editor = (TextBox)page.FindName("HostsTextBox");
        page.SetHostsContent("127.0.0.1 localhost", "Loaded");
        Assert(!page.HasUnsavedHostsChanges, "Loaded Hosts content marked dirty.");
        editor.Text += "\r\n127.0.0.1 example.test";
        Assert(page.HasUnsavedHostsChanges, "Hosts edit not marked dirty.");
        page.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        Assert(page.HasUnsavedHostsChanges && editor.Text.Contains("example.test"),
            "Visibility change lost Hosts edits.");

        var saved = editor.Text;
        editor.Text += "\r\n127.0.0.1 newer.test";
        page.MarkHostsSaved(saved, "Saved earlier content");
        Assert(page.HasUnsavedHostsChanges && editor.Text.Contains("newer.test"),
            "Save completion discarded newer edits.");
        page.MarkHostsSaved(editor.Text, "Saved");
        Assert(!page.HasUnsavedHostsChanges, "Successful save did not reset baseline.");
        editor.Text += "\r\n# unsaved";
        page.SetHostsStatus("Save failed");
        Assert(page.HasUnsavedHostsChanges, "Failed save reset the baseline.");
        page.SetHostsContent(saved, "Reloaded");
        Assert(!page.HasUnsavedHostsChanges && editor.Text == saved,
            "Explicit reload did not reset content and baseline.");
    }

    private static async Task VerifyRenameAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DuoLaBoxRegression-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var files = Enumerable.Range(1, 3)
                .Select(index => Path.Combine(directory, $"{index}.txt"))
                .ToArray();
            for (var index = 0; index < files.Length; index++)
            {
                File.WriteAllText(files[index], $"original {index + 1}");
            }

            var blockedDestination = Path.Combine(directory, "4.txt");
            Directory.CreateDirectory(blockedDestination);
            var service = new BatchRenameService();
            var plan = service.CreatePlan(files, "{index}{ext}", 2);
            // The existing directory makes the third final move fail after two overlapping moves.
            await ExpectRenameFailureAsync(service, plan);
            for (var index = 0; index < files.Length; index++)
            {
                Assert(File.ReadAllText(files[index]) == $"original {index + 1}",
                    "Overlapping rename rollback did not restore the original file.");
            }
            Assert(!Directory.EnumerateFiles(directory, ".duolabox-*.tmp").Any(),
                "Rollback left temporary files.");

            Directory.Delete(blockedDestination);
            await service.ExecuteAsync(plan);
            for (var index = 0; index < plan.Count; index++)
            {
                Assert(File.ReadAllText(plan[index].DestinationPath) == $"original {index + 1}",
                    "Successful overlapping rename changed file contents.");
            }

            var left = plan[0].DestinationPath;
            var right = plan[1].DestinationPath;
            await service.ExecuteAsync([new(left, right), new(right, left)]);
            Assert(File.ReadAllText(left) == "original 2" && File.ReadAllText(right) == "original 1",
                "Swapping file names failed.");

            await ExpectRenameFailureAsync(service,
                [new(left, Path.Combine(directory, "left.txt")),
                 new(Path.Combine(directory, "missing.txt"), Path.Combine(directory, "missing-new.txt"))]);
            Assert(File.ReadAllText(left) == "original 2" &&
                   !Directory.EnumerateFiles(directory, ".duolabox-*.tmp").Any(),
                "Staging failure did not restore already staged files.");
        }
        finally
        {
            // Only remove files inside the fresh, uniquely named test directory.
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ExpectRenameFailureAsync(
        BatchRenameService service, IReadOnlyList<RenamePreviewItem> plan)
    {
        try
        {
            await service.ExecuteAsync(plan);
        }
        catch (IOException)
        {
            return;
        }

        throw new InvalidOperationException("Expected rename failure did not occur.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
