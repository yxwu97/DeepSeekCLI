using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IHarnessLifecycleCoordinator : IAsyncDisposable
{
    HarnessStateSnapshot Current { get; }
    event EventHandler<HarnessStateSnapshot>? StateChanged;
    Task InitializeAsync(CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task RestartAsync(CancellationToken cancellationToken);
    Task ApplyServiceUriAsync(Uri serviceUri, CancellationToken cancellationToken);
}
