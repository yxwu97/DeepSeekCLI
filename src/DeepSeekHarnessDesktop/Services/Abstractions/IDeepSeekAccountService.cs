using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDeepSeekAccountService
{
    Task<DeepSeekAccountSnapshot> GetBalanceAsync(
        string apiKey,
        CancellationToken cancellationToken);
}
