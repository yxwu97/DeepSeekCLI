using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace DeepSeekHarnessDesktop.Services;

public sealed class SingleInstanceService : IAsyncDisposable
{
    private const string ActivateCommand = "Activate";
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _listenerCts = new();
    private Task? _listenerTask;

    public SingleInstanceService(string? instanceId = null)
    {
        instanceId ??= GetCurrentUserIdentifier();
        var safeId = instanceId.Replace('\\', '-').Replace('/', '-');
        _mutex = new Mutex(false, $@"Local\DeepSeekHarnessDesktop-{safeId}", out var createdNew);
        _pipeName = $"DeepSeekHarnessDesktop-{safeId}";
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void StartListening(Func<Task> activateAsync)
    {
        ArgumentNullException.ThrowIfNull(activateAsync);
        if (!IsPrimary)
        {
            throw new InvalidOperationException("Only the primary instance can listen for activation.");
        }
        if (_listenerTask is not null)
        {
            throw new InvalidOperationException("The activation listener is already running.");
        }

        _listenerTask = ListenAsync(activateAsync, _listenerCts.Token);
    }

    public async Task<bool> NotifyPrimaryAsync(CancellationToken cancellationToken)
    {
        if (IsPrimary)
        {
            return false;
        }

        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(2000, cancellationToken);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: false)
            {
                AutoFlush = true,
            };
            await writer.WriteLineAsync(ActivateCommand.AsMemory(), cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ListenAsync(Func<Task> activateAsync, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var command = await reader.ReadLineAsync(cancellationToken);
                if (string.Equals(command, ActivateCommand, StringComparison.Ordinal))
                {
                    await activateAsync();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                // Recreate the pipe after a client disconnects unexpectedly.
            }
        }
    }

    private static string GetCurrentUserIdentifier() =>
        WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;

    public async ValueTask DisposeAsync()
    {
        _listenerCts.Cancel();
        if (_listenerTask is not null)
        {
            try
            {
                await _listenerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _listenerCts.Dispose();
        _mutex.Dispose();
    }
}
