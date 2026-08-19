using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IPrivateDshInstallationStore
{
    Task<DshInstallationCandidate?> FindActiveAsync(
        string? nodePath,
        CancellationToken cancellationToken);
    Task<PrivateDshInstallTransaction> CreateTransactionAsync(CancellationToken cancellationToken);
    Task<DshInstallationCandidate> CommitVersionAsync(
        PrivateDshInstallTransaction transaction,
        string nodePath,
        CancellationToken cancellationToken);
    Task ActivateAsync(
        DshInstallationCandidate candidate,
        CancellationToken cancellationToken);
    Task CleanupAsync(PrivateDshInstallTransaction transaction);
}
