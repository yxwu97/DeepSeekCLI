using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshCatalogService
{
    Task<DshCatalogLoadResult> LoadAsync(CancellationToken cancellationToken);
}
