using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using NuGet.Versioning;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCatalogUpdateInstaller : IDshCatalogUpdateInstaller
{
    private readonly IDshCatalogAssetDownloader _downloader;
    private readonly IPrivateDshInstallationStore _store;
    private readonly INpmInstallRunner _installRunner;
    private readonly IDshCandidateSmokeVerifier _smokeVerifier;
    private readonly IDshTrustedVersionPolicy _trustedVersions;
    private readonly IRecentLogBuffer? _recentLogs;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DshCatalogUpdateInstaller(
        IDshCatalogAssetDownloader downloader,
        IPrivateDshInstallationStore store,
        INpmInstallRunner installRunner,
        IDshCandidateSmokeVerifier smokeVerifier,
        IDshTrustedVersionPolicy trustedVersions,
        IRecentLogBuffer? recentLogs = null)
    {
        _downloader = downloader;
        _store = store;
        _installRunner = installRunner;
        _smokeVerifier = smokeVerifier;
        _trustedVersions = trustedVersions;
        _recentLogs = recentLogs;
    }

    public async Task<DshInstallationCandidate> InstallAsync(
        DshCatalogUpdateCandidate target,
        string desktopVersion,
        string nodeVersion,
        string nodePath,
        string npmPath,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        DshDownloadedAssets? assets = null;
        PrivateDshInstallTransaction? transaction = null;
        try
        {
            ValidateTarget(target, desktopVersion, nodeVersion);
            _recentLogs?.AddDesktop($"开始下载签名目录 DSH {target.Entry.Version} 的锁定资源。");
            assets = await _downloader.DownloadAsync(target.Entry, cancellationToken);
            transaction = await _store.CreateTransactionAsync(
                target.Descriptor,
                assets.PackagePath,
                assets.LockPath,
                cancellationToken);
            await _installRunner.RunAsync(npmPath, transaction.StagingPath, cancellationToken);
            var candidate = await _store.CommitVersionAsync(
                transaction,
                nodePath,
                cancellationToken);
            await _smokeVerifier.VerifyAsync(candidate, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Once commit starts, complete both pointers without observing user cancellation.
            await _store.ActivateAsync(candidate, CancellationToken.None);
            await _trustedVersions.SelectAsync(target.Descriptor, CancellationToken.None);
            _recentLogs?.AddDesktop($"签名目录 DSH 已验证并激活：{candidate.InstallId}。");
            return candidate;
        }
        finally
        {
            try
            {
                if (transaction is not null)
                {
                    await CleanupTransactionAsync(transaction);
                }
            }
            finally
            {
                try
                {
                    if (assets is not null)
                    {
                        await CleanupAssetsAsync(assets);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
    }

    private void ValidateTarget(
        DshCatalogUpdateCandidate target,
        string desktopVersion,
        string nodeVersion)
    {
        var selected = DshCatalogCompatibility.SelectUpdate(
            new DshCatalogSnapshot(
                1,
                target.CatalogSequence,
                DateTimeOffset.Now,
                "already-verified",
                [target.Entry]),
            _trustedVersions.Current,
            desktopVersion,
            nodeVersion,
            out var error);
        if (selected is null
            || !string.Equals(selected.Descriptor.Version, target.Descriptor.Version, StringComparison.Ordinal)
            || !string.Equals(selected.Descriptor.EvidenceSha256, target.Descriptor.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new HarnessException(error ?? DshUpdateErrorMapper.CatalogRejected(
                "The selected signed DSH update is stale or does not match its verified catalog descriptor."));
        }
        if (!NuGetVersion.TryParse(target.Entry.Version, out _))
        {
            throw new HarnessException(DshUpdateErrorMapper.CatalogRejected(
                "The selected signed DSH update version is invalid."));
        }
    }

    private async Task CleanupTransactionAsync(PrivateDshInstallTransaction transaction)
    {
        try { await _store.CleanupAsync(transaction); }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or HarnessException or ArgumentException)
        {
            _recentLogs?.AddDesktop($"安装暂存清理失败：{exception.GetType().Name}。");
        }
    }

    private async Task CleanupAssetsAsync(DshDownloadedAssets assets)
    {
        try { await _downloader.CleanupAsync(assets); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HarnessException)
        {
            _recentLogs?.AddDesktop($"下载暂存清理失败：{exception.GetType().Name}。");
        }
    }
}
