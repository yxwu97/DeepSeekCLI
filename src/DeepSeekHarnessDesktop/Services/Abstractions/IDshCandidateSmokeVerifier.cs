using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshCandidateSmokeVerifier
{
    Task VerifyAsync(DshInstallationCandidate candidate, CancellationToken cancellationToken);
}
