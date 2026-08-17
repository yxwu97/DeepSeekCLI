using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface ICodeWebViewService
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task NavigateAsync(Uri uri, CancellationToken cancellationToken);
    Task ReloadAsync(CancellationToken cancellationToken);
    Task ShowLocalStateAsync(HarnessRuntimeState state, HarnessError? error, CancellationToken cancellationToken);
}
