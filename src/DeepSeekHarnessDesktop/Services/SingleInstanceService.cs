using System.IO.Pipes;
using System.Security.AccessControl;
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
        if (activateAsync is null) throw new ArgumentNullException(nameof(activateAsync));
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
            using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(2000, cancellationToken);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: false)
            {
                AutoFlush = true,
            };
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(ActivateCommand);
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
                using var server = CreateServerStream();
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, true, 1024, leaveOpen: true);
                var command = await reader.ReadLineAsync().WaitAsync(cancellationToken);
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

    private NamedPipeServerStream CreateServerStream()
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        return new NamedPipeServerStream(
            _pipeName,
            PipeDirection.In,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }

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
