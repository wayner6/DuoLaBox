using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace DuoLaBox.Services;

public sealed class SingleInstanceService : IDisposable
{
    internal const int MaximumArgumentPayloadLength = 64 * 1024;
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly int _connectionTimeoutMilliseconds;
    private Task? _listenerTask;
    private int _disposed;

    public SingleInstanceService() : this(GetInstanceName())
    {
    }

    internal SingleInstanceService(string instanceName, int connectionTimeoutMilliseconds = 3000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectionTimeoutMilliseconds);
        _connectionTimeoutMilliseconds = connectionTimeoutMilliseconds;
        _pipeName = instanceName + ".Arguments";
        _mutex = new Mutex(false, "Local\\" + instanceName + ".Application");
        try
        {
            IsPrimary = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }
    }

    public bool IsPrimary { get; }

    // Invoked on the listener thread; enqueue UI work instead of synchronously waiting for it.
    public event Action<string[]>? ArgumentsReceived;

    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!IsPrimary || _listenerTask is not null)
        {
            return;
        }
        if (ArgumentsReceived is null)
        {
            throw new InvalidOperationException("必须先注册参数接收处理程序。");
        }
        _listenerTask = Task.Run(ListenAsync);
    }

    public async Task<bool> ForwardArgumentsAsync(string[] arguments, int timeoutMilliseconds = 3000)
    {
        if (IsPrimary || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        try
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMilliseconds);
            var payload = JsonSerializer.SerializeToUtf8Bytes(arguments);
            if (payload.Length > MaximumArgumentPayloadLength || arguments.Any(argument => argument is null))
            {
                return false;
            }
            using var timeout = new CancellationTokenSource(timeoutMilliseconds);
            await using var pipe = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await pipe.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            var acknowledgement = new byte[1];
            await pipe.ReadExactlyAsync(acknowledgement, timeout.Token).ConfigureAwait(false);
            return acknowledgement[0] == 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            return false;
        }
    }

    private async Task ListenAsync()
    {
        var cancellationToken = _cancellation.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_connectionTimeoutMilliseconds);
                var header = new byte[sizeof(int)];
                await pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is < 1 or > MaximumArgumentPayloadLength)
                {
                    continue;
                }
                var payload = new byte[length];
                await pipe.ReadExactlyAsync(payload, timeout.Token).ConfigureAwait(false);
                var arguments = JsonSerializer.Deserialize<string[]>(payload);
                if (arguments is null || arguments.Any(argument => argument is null))
                {
                    continue;
                }
                var receiver = ArgumentsReceived;
                if (receiver is null)
                {
                    continue;
                }
                timeout.Token.ThrowIfCancellationRequested();
                receiver(arguments);
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }

    // Dispose primary instances on their creating thread: mutex ownership is thread-affine.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _cancellation.Cancel();
        try
        {
            _listenerTask?.GetAwaiter().GetResult();
        }
        finally
        {
            _cancellation.Dispose();
            try
            {
                if (IsPrimary)
                {
                    _mutex.ReleaseMutex();
                }
            }
            finally
            {
                _mutex.Dispose();
            }
        }
    }

    private static string GetInstanceName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return $"{AppIdentity.Name}.{identity.User?.Value ?? Environment.UserName}.{process.SessionId}";
    }
}
