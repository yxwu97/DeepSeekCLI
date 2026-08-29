using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshRuntimeUpdateCoordinator
{
    Task<DshInstallationCandidate> ApplyAsync(
        DshCatalogUpdateCandidate target,
        DependencyDiagnosticsResult diagnostics,
        CancellationToken cancellationToken);
}
