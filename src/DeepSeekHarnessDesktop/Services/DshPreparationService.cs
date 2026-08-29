using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshPreparationService : IDshPreparationService
{
    private readonly IDshCandidateDiscoveryService _discovery;
    private readonly IPrivateDshInstallationStore _store;
    private readonly INpmInstallRunner _installRunner;
    private readonly IRecentLogBuffer? _recentLogs;
    private readonly IDshCandidateSmokeVerifier _smokeVerifier;

    public DshPreparationService(
        IDshCandidateDiscoveryService discovery,
        IPrivateDshInstallationStore store,
        INpmInstallRunner installRunner,
        IHarnessProcessManager processManager,
        IHarnessHealthMonitor healthMonitor,
        IRecentLogBuffer? recentLogs = null,
        IDshCandidateSmokeVerifier? smokeVerifier = null)
    {
        _discovery = discovery;
        _store = store;
        _installRunner = installRunner;
        _recentLogs = recentLogs;
        _smokeVerifier = smokeVerifier
            ?? new DshCandidateSmokeVerifier(processManager, healthMonitor, recentLogs);
    }

    public async Task<bool> RequiresPreparationAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.Launch.Mode == LaunchMode.Custom)
        {
            return false;
        }
        return (await _discovery.DiscoverAsync(cancellationToken)).Candidate is null;
    }

    public async Task PrepareAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Launch.Mode == LaunchMode.Custom)
        {
            return;
        }
        var discovery = await _discovery.DiscoverAsync(cancellationToken);
        if (discovery.Candidate is not null)
        {
            return;
        }
        if (!discovery.CanPrepare)
        {
            throw new HarnessException(new HarnessError(
                "DSH-E101",
                "请先安装 Node.js LTS x64",
                "Locked DSH preparation requires node.exe and npm.cmd on PATH.",
                true));
        }

        PrivateDshInstallTransaction? transaction = null;
        try
        {
            _recentLogs?.AddDesktop("未发现可复用 DSH，开始当前用户私有锁定安装。");
            transaction = await _store.CreateTransactionAsync(cancellationToken);
            await _installRunner.RunAsync(
                discovery.NpmPath!,
                transaction.StagingPath,
                cancellationToken);
            var candidate = await _store.CommitVersionAsync(
                transaction,
                discovery.NodePath!,
                cancellationToken);
            await _smokeVerifier.VerifyAsync(candidate, cancellationToken);
            await _store.ActivateAsync(candidate, cancellationToken);
            _recentLogs?.AddDesktop($"DSH 私有安装已激活：{candidate.InstallId}。");
        }
        catch (HarnessException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new HarnessException(new HarnessError(
                "DSH-E214",
                "DSH 私有安装目录不可写",
                exception.Message,
                true,
                exception));
        }
        finally
        {
            if (transaction is not null)
            {
                try { await _store.CleanupAsync(transaction); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _recentLogs?.AddDesktop($"安装暂存清理失败：{exception.GetType().Name}。");
                }
            }
        }
    }

}
