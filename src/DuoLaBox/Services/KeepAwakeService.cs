namespace DuoLaBox.Services;

public sealed class KeepAwakeService : IDisposable
{
    public bool IsEnabled { get; private set; }

    public bool KeepDisplayOn { get; private set; }

    public bool SetEnabled(bool enabled, bool keepDisplayOn)
    {
        var flags = NativeMethods.EsContinuous;
        if (enabled)
        {
            flags |= NativeMethods.EsSystemRequired;
            if (keepDisplayOn)
            {
                flags |= NativeMethods.EsDisplayRequired;
            }
        }

        if (NativeMethods.SetThreadExecutionState(flags) == 0)
        {
            return false;
        }

        IsEnabled = enabled;
        KeepDisplayOn = enabled && keepDisplayOn;
        return true;
    }

    public void Dispose()
    {
        SetEnabled(enabled: false, keepDisplayOn: false);
    }
}
