using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshCatalogAssetDownloader
{
    Task<DshDownloadedAssets> DownloadAsync(
        DshCatalogEntry entry,
        CancellationToken cancellationToken);
    Task CleanupAsync(DshDownloadedAssets assets);
}
