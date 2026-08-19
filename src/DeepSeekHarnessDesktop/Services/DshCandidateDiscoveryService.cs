using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCandidateDiscoveryService : IDshCandidateDiscoveryService
{
    private readonly EnvironmentPathProvider _pathProvider;
    private readonly IPrivateDshInstallationStore _privateStore;
    private readonly NpxDshCacheLocator _cacheLocator;

    public DshCandidateDiscoveryService(
        EnvironmentPathProvider? pathProvider = null,
        IPrivateDshInstallationStore? privateStore = null,
        NpxDshCacheLocator? cacheLocator = null)
    {
        _pathProvider = pathProvider ?? new EnvironmentPathProvider();
        _privateStore = privateStore ?? new PrivateDshInstallationStore();
        _cacheLocator = cacheLocator ?? new NpxDshCacheLocator();
    }

    public async Task<DshDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var globalDsh = _pathProvider.FindOnPath("dsh.cmd");
        var node = _pathProvider.FindOnPath("node.exe");
        var npm = _pathProvider.FindOnPath("npm.cmd");
        var npx = _pathProvider.FindOnPath("npx.cmd");

        if (globalDsh is not null)
        {
            return Result(new DshInstallationCandidate(
                DshInstallationSource.GlobalPath,
                globalDsh,
                null,
                DshPackageMetadata.ValidatedVersion));
        }

        var privateDsh = await _privateStore.FindActiveAsync(node, cancellationToken);
        if (privateDsh is not null)
        {
            return Result(privateDsh);
        }

        var cachedDsh = await _cacheLocator.FindAsync(node, cancellationToken);
        return Result(cachedDsh is null
            ? null
            : new DshInstallationCandidate(
                DshInstallationSource.NpxCache,
                cachedDsh.NodePath,
                cachedDsh.EntryPointPath,
                cachedDsh.Version));

        DshDiscoveryResult Result(DshInstallationCandidate? candidate) =>
            new(candidate, node, npm, npx);
    }
}
