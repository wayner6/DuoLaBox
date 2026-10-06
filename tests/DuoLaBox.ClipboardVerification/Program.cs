using System.Windows;
using DuoLaBox.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--allow-clipboard-write"))
        {
            Console.Error.WriteLine(
                "This check replaces clipboard contents. Pass --allow-clipboard-write to run.");
            return 2;
        }

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        application.Startup += async (_, _) =>
        {
            try
            {
                var service = new ClipboardService();
                foreach (var text in new[] { "#1689D8", "rgb(22, 137, 216)", "取色验证 🎨\r\n第二行" })
                {
                    if (!await service.CopyTextAsync(text) || Clipboard.GetText() != text)
                    {
                        throw new InvalidOperationException("UI-thread clipboard round-trip failed.");
                    }

                    if (!await Task.Run(() => service.CopyTextAsync(text)) || Clipboard.GetText() != text)
                    {
                        throw new InvalidOperationException("Worker-thread clipboard round-trip failed.");
                    }
                }

                Console.WriteLine("CLIPBOARD_VERIFICATION_OK");
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
}
