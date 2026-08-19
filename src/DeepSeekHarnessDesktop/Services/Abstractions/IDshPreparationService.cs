using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshPreparationService
{
    Task<bool> RequiresPreparationAsync(AppSettings settings, CancellationToken cancellationToken);
    Task PrepareAsync(AppSettings settings, CancellationToken cancellationToken);
}
