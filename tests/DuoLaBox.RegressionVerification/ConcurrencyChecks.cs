using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DuoLaBox.Models;
using DuoLaBox.Services;
using DuoLaBox.Views;

internal static class ConcurrencyChecks
{
    internal static async Task RunAsync()
    {
        VerifySettingsControls();
        await VerifyFileQueueAsync();
        await VerifyPipesAsync();
        VerifyAbandonedMutex();
    }

    private static void VerifySettingsControls()
    {
        var system = new SystemToolsPage();
        system.SetHostsBusy(true);
        Assert(!((Button)system.FindName("HostsSaveButton")).IsEnabled &&
               !((Button)system.FindName("HostsReloadButton")).IsEnabled &&
               ((TextBox)system.FindName("HostsTextBox")).IsEnabled,
            "Hosts save did not disable duplicate actions or blocked editing.");
        system.SetHostsBusy(false);
        Assert(((Button)system.FindName("HostsSaveButton")).IsEnabled, "Hosts save stayed disabled.");
        system.SetThemeBusy(true);
        Assert(!((ToggleButton)system.FindName("NightModeToggle")).IsEnabled &&
               !((ToggleButton)system.FindName("ScheduleToggle")).IsEnabled,
            "Theme change did not block duplicate controls.");
        system.SetThemeBusy(false);
        Assert(((ToggleButton)system.FindName("NightModeToggle")).IsEnabled, "Theme control stayed disabled.");
        var settings = new SettingsPage();
        settings.SetStartupBusy(true);
        Assert(!((ToggleButton)settings.FindName("StartWithWindowsToggle")).IsEnabled &&
               !((ToggleButton)settings.FindName("SilentStartupToggle")).IsEnabled,
            "Startup settings were not protected together.");
        settings.SetStartupBusy(false);
    }

    private static async Task VerifyFileQueueAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DuoLaBoxQueue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "first.txt");
            var second = Path.Combine(directory, "second.txt");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");
            var page = new FileBatchPage();
            page.SetContextMenusBusy(true);
            Assert(!((ToggleButton)page.FindName("RenameContextMenuButton")).IsEnabled &&
                   !((ToggleButton)page.FindName("UnlockContextMenuButton")).IsEnabled &&
                   !((ToggleButton)page.FindName("ResizeContextMenuButton")).IsEnabled,
                "Context menu changes were not protected together.");
            page.SetContextMenusBusy(false);
            page.Open(FileToolMode.Rename, [first]);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var active = page.RunBusyAsync(() => release.Task);
            var queuedPaths = new List<string> { second };
            page.Open(FileToolMode.Rename, queuedPaths);
            queuedPaths.Clear();
            page.Open(FileToolMode.ResizeImages, []);
            Assert(page.IsBusy && ((Grid)page.FindName("RenamePanel")).Visibility == Visibility.Visible &&
                   ((ListBox)page.FindName("SelectedFilesList")).Items.Count == 1,
                "Queued request changed the active mode or file list.");
            var duplicateRan = false;
            await page.RunBusyAsync(() =>
            {
                duplicateRan = true;
                return Task.CompletedTask;
            });
            Assert(!duplicateRan && !((Border)page.FindName("FileSelectionPanel")).IsEnabled,
                "Duplicate operation bypassed the busy guard.");
            release.SetResult(true);
            await active;
            Assert(!page.IsBusy && ((Grid)page.FindName("ResizePanel")).Visibility == Visibility.Visible &&
                   ((ListBox)page.FindName("RenamePreviewList")).Items.Count == 2,
                "Queued files were not snapshotted or drained after completion.");
            Assert(((Border)page.FindName("FileSelectionPanel")).IsEnabled,
                "File controls stayed disabled after completion.");

            page.Open(FileToolMode.Rename, []);
            var execute = (Button)page.FindName("ExecuteRenameButton");
            execute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var overlay = (Grid)page.FindName("ModernConfirmOverlay");
            Assert(page.IsBusy && overlay.Visibility == Visibility.Visible && overlay.IsEnabled,
                "Rename confirmation was not protected or cannot be clicked.");
            page.Open(FileToolMode.ResizeImages, []);
            var confirmPanel = (Grid)((Border)overlay.Children[0]).Child;
            var buttons = confirmPanel.Children.OfType<StackPanel>().Single();
            var cancel = buttons.Children.OfType<Button>().Single(button => (string)button.Content == "取消");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (page.IsBusy)
            {
                await Task.Delay(10, timeout.Token);
            }
            Assert(((Grid)page.FindName("ResizePanel")).Visibility == Visibility.Visible &&
                   File.ReadAllText(first) == "first" && File.ReadAllText(second) == "second",
                "Canceling confirmation did not drain requests or changed files.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task VerifyPipesAsync()
    {
        var instanceName = $"DuoLaBoxPipeTest-{Guid.NewGuid():N}";
        using var primary = new SingleInstanceService(instanceName, connectionTimeoutMilliseconds: 1000);
        using var secondary = await Task.Run(() => new SingleInstanceService(instanceName));
        Assert(primary.IsPrimary && !secondary.IsPrimary, "Single-instance election failed.");
        var rejectedUnsubscribedStart = false;
        try
        {
            primary.StartListening();
        }
        catch (InvalidOperationException)
        {
            rejectedUnsubscribedStart = true;
        }
        Assert(rejectedUnsubscribedStart, "Listener started before a receiver was registered.");
        var received = new ConcurrentQueue<string[]>();
        primary.ArgumentsReceived += received.Enqueue;
        primary.StartListening();
        primary.StartListening();
        Assert(await secondary.ForwardArgumentsAsync(["--file-tool", "rename", "测试文件.txt"]),
            "Valid arguments were not acknowledged.");
        Assert(received.TryDequeue(out var arguments) && arguments.SequenceEqual(
                new[] { "--file-tool", "rename", "测试文件.txt" }),
            "Forwarded arguments changed.");
        Assert(!await secondary.ForwardArgumentsAsync(["--show"], timeoutMilliseconds: -1),
            "Invalid timeout allowed an unbounded forwarding operation.");
        Assert(!await secondary.ForwardArgumentsAsync([new string('x', SingleInstanceService.MaximumArgumentPayloadLength)]),
            "Oversized outgoing arguments were accepted.");

        foreach (var length in new[] { -1, 0, SingleInstanceService.MaximumArgumentPayloadLength + 1 })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await using var pipe = await ConnectAsync(instanceName, timeout.Token);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, length);
            await pipe.WriteAsync(header, timeout.Token);
            var acknowledgement = new byte[1];
            Assert(await pipe.ReadAsync(acknowledgement, timeout.Token) == 0,
                "Invalid frame length was acknowledged.");
        }
        foreach (var text in new[] { "not JSON", "[null]", "null" })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await using var pipe = await ConnectAsync(instanceName, timeout.Token);
            var payload = Encoding.UTF8.GetBytes(text);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await pipe.WriteAsync(header, timeout.Token);
            await pipe.WriteAsync(payload, timeout.Token);
            Assert(await pipe.ReadAsync(new byte[1], timeout.Token) == 0,
                "Invalid arguments were acknowledged.");
        }
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            await using var stalled = await ConnectAsync(instanceName, timeout.Token);
            Assert(await stalled.ReadAsync(new byte[1], timeout.Token) == 0,
                "A stalled connection did not time out.");
        }
        Assert(received.IsEmpty && await secondary.ForwardArgumentsAsync(["--show"]),
            "Bad connections poisoned subsequent forwarding.");
        Assert(received.TryDequeue(out var show) && show.SequenceEqual(new[] { "--show" }),
            "Recovery forwarded the wrong arguments.");

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            await using var connected = await ConnectAsync(instanceName, timeout.Token);
            primary.Dispose();
            primary.Dispose();
            Assert(await connected.ReadAsync(new byte[1], timeout.Token) == 0,
                "Disposal did not stop an active connection.");
        }
        Assert(!await secondary.ForwardArgumentsAsync(["--show"], timeoutMilliseconds: 100),
            "Forwarding to a stopped listener succeeded.");
        using var replacement = new SingleInstanceService(instanceName);
        Assert(replacement.IsPrimary, "A secondary mutex handle prevented primary ownership recovery.");
    }

    private static void VerifyAbandonedMutex()
    {
        var instanceName = $"DuoLaBoxAbandonedTest-{Guid.NewGuid():N}";
        using var ownerHandle = new Mutex(false, "Local\\" + instanceName + ".Application");
        var owner = new Thread(() => ownerHandle.WaitOne()) { IsBackground = true };
        owner.Start();
        Assert(owner.Join(TimeSpan.FromSeconds(3)), "Test mutex owner did not exit.");
        using var replacement = new SingleInstanceService(instanceName);
        Assert(replacement.IsPrimary, "Abandoned mutex ownership was not recovered.");
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string instanceName, CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(
            ".", instanceName + ".Arguments", PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(token);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
