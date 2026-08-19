using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshCandidateDiscoveryService
{
    Task<DshDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
}
