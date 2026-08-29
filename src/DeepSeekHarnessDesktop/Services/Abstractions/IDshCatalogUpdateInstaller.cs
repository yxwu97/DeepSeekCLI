using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshCatalogUpdateInstaller
{
    Task<DshInstallationCandidate> InstallAsync(
        DshCatalogUpdateCandidate target,
        string desktopVersion,
        string nodeVersion,
        string nodePath,
        string npmPath,
        CancellationToken cancellationToken);
}
