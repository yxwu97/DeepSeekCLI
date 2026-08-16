using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IHarnessProcessManager : IAsyncDisposable
{
    event EventHandler<ProcessOutputEventArgs>? OutputReceived;
    event EventHandler<ProcessExitedEventArgs>? ProcessExited;
    HarnessProcessInfo? Current { get; }
    bool IsRunning { get; }
    Task<HarnessProcessInfo> StartAsync(DshLaunchOptions options, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
