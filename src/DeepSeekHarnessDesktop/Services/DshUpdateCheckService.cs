using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using NuGet.Versioning;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshUpdateCheckService : IDshUpdateCheckService
{
    private readonly IDshReleaseService _releaseService;
    private readonly IDshCatalogService _catalogService;
    private readonly IDshTrustedVersionPolicy _trustedVersions;

    public DshUpdateCheckService(
        IDshReleaseService releaseService,
        IDshCatalogService catalogService,
        IDshTrustedVersionPolicy trustedVersions)
    {
        _releaseService = releaseService;
        _catalogService = catalogService;
        _trustedVersions = trustedVersions;
    }

    public async Task<DshCombinedUpdateCheckResult> CheckAsync(
        string desktopVersion,
        string? nodeVersion,
        CancellationToken cancellationToken)
    {
        var releaseTask = _releaseService.CheckLatestAsync(cancellationToken);
        var catalogTask = _catalogService.LoadAsync(cancellationToken);
        await Task.WhenAll(releaseTask, catalogTask);
        var release = await releaseTask;
        var catalog = await catalogTask;
        var current = _trustedVersions.Current;
        DshCatalogUpdateCandidate? update = null;
        HarnessError? catalogError = catalog.Error;
        if (catalog.Snapshot is not null && catalogError is null)
        {
            update = DshCatalogCompatibility.SelectUpdate(
                catalog.Snapshot,
                current,
                desktopVersion,
                nodeVersion,
                out catalogError);
        }

        var waiting = release.LatestVersion is { } latestText
            && NuGetVersion.TryParse(latestText, out var latest)
            && NuGetVersion.TryParse(current.Version, out var selected)
            && latest > selected
            && (update is null || latest > NuGetVersion.Parse(update.Entry.Version));
        return new DshCombinedUpdateCheckResult(
            current.Version,
            release.LatestVersion,
            update,
            waiting,
            DateTimeOffset.Now,
            release.ErrorMessage,
            catalogError);
    }
}
