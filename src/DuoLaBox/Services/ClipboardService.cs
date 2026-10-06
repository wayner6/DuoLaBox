using System.Runtime.InteropServices;

namespace DuoLaBox.Services;

public sealed class ClipboardService
{
    public async Task<bool> CopyTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await dispatcher.InvokeAsync(() => System.Windows.Clipboard.SetText(text));
                return true;
            }
            catch (ExternalException)
            {
                if (attempt < 9)
                {
                    await Task.Delay(18);
                }
            }
        }

        return false;
    }
}
