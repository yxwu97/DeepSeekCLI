using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshUpdateCheckService
{
    Task<DshCombinedUpdateCheckResult> CheckAsync(
        string desktopVersion,
        string? nodeVersion,
        CancellationToken cancellationToken);
}
