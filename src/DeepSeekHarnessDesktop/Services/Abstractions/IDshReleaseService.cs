using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshReleaseService
{
    Task<DshUpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken);
}
