namespace DuoLaBox;

public partial class MainWindow
{
    private bool _systemInformationLoaded;

    private async void SystemInformationPage_RefreshRequested()
    {
        await RefreshSystemInformationAsync();
    }

    private async Task RefreshSystemInformationAsync()
    {
        _systemInformationCancellation?.Cancel();
        _systemInformationCancellation?.Dispose();
        _systemInformationCancellation = new CancellationTokenSource();
        var cancellationToken =
            _systemInformationCancellation.Token;
        SystemInformationPage.SetScanning();
        try
        {
            var snapshot = await _systemInformationService.ScanAsync(
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SystemInformationPage.SetSnapshot(snapshot);
            _systemInformationLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // A newer scan has replaced this one.
        }
        catch (Exception exception)
        {
            SystemInformationPage.SetError(exception.Message);
        }
    }
}
