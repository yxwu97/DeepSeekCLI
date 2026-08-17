using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IChatWebViewService
{
    ChatPageSnapshot Current { get; }
    bool IsInitialized { get; }
    event EventHandler<ChatPageSnapshot>? StateChanged;
    Task InitializeAsync(CancellationToken cancellationToken);
    Task ReloadAsync(CancellationToken cancellationToken);
    Task ClearBrowsingDataAsync(CancellationToken cancellationToken);
}
