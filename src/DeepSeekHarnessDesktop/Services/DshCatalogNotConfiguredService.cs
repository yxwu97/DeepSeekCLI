using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCatalogNotConfiguredService : IDshCatalogService
{
    public Task<DshCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DshCatalogLoadResult(
            null,
            false,
            DateTimeOffset.Now,
            DshUpdateErrorMapper.CatalogRejected(
                "The production DSH catalog trust anchor and fixed endpoint are not configured in this Desktop build.")));
    }
}
