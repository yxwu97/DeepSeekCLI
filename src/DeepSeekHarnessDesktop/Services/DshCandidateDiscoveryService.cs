using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCandidateDiscoveryService : IDshCandidateDiscoveryService
{
    private readonly EnvironmentPathProvider _pathProvider;
    private readonly IPrivateDshInstallationStore _privateStore;
    private readonly NpxDshCacheLocator _cacheLocator;
    private readonly IDshVersionProbe _versionProbe;

    public DshCandidateDiscoveryService(
        EnvironmentPathProvider? pathProvider = null,
        IPrivateDshInstallationStore? privateStore = null,
        NpxDshCacheLocator? cacheLocator = null,
        IDshVersionProbe? versionProbe = null)
    {
        _pathProvider = pathProvider ?? new EnvironmentPathProvider();
        _privateStore = privateStore ?? new PrivateDshInstallationStore();
        _cacheLocator = cacheLocator ?? new NpxDshCacheLocator();
        _versionProbe = versionProbe ?? new DshVersionProbe();
    }

    public async Task<DshDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var globalDsh = _pathProvider.FindOnPath("dsh.cmd");
        var node = _pathProvider.FindOnPath("node.exe");
        var npm = _pathProvider.FindOnPath("npm.cmd");
        var npx = _pathProvider.FindOnPath("npx.cmd");

        DshCandidateRejection? rejectedGlobal = null;
        if (globalDsh is not null)
        {
            var probe = await _versionProbe.ProbeAsync(globalDsh, cancellationToken);
            if (probe.Succeeded
                && string.Equals(
                    probe.Version,
                    DshPackageMetadata.ValidatedVersion,
                    StringComparison.Ordinal))
            {
                return Result(new DshInstallationCandidate(
                    DshInstallationSource.GlobalPath,
                    globalDsh,
                    null,
                    probe.Version!));
            }

            rejectedGlobal = new DshCandidateRejection(
                probe.Version,
                probe.Detail ?? $"Global DSH version must be {DshPackageMetadata.ValidatedVersion}.");
        }

        var privateDsh = await _privateStore.FindActiveAsync(node, cancellationToken);
        if (privateDsh is not null)
        {
            return Result(privateDsh, rejectedGlobal);
        }

        var cachedDsh = await _cacheLocator.FindAsync(node, cancellationToken);
        return Result(cachedDsh is null
            ? null
            : new DshInstallationCandidate(
                DshInstallationSource.NpxCache,
                cachedDsh.NodePath,
                cachedDsh.EntryPointPath,
                cachedDsh.Version), rejectedGlobal);

        DshDiscoveryResult Result(
            DshInstallationCandidate? candidate,
            DshCandidateRejection? rejection = null) =>
            new(candidate, node, npm, npx, rejection);
    }
}
