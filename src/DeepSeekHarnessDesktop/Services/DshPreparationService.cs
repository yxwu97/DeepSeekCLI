using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshPreparationService : IDshPreparationService
{
    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromSeconds(30);
    private readonly IDshCandidateDiscoveryService _discovery;
    private readonly IPrivateDshInstallationStore _store;
    private readonly INpmInstallRunner _installRunner;
    private readonly IHarnessProcessManager _processManager;
    private readonly IHarnessHealthMonitor _healthMonitor;
    private readonly IRecentLogBuffer? _recentLogs;

    public DshPreparationService(
        IDshCandidateDiscoveryService discovery,
        IPrivateDshInstallationStore store,
        INpmInstallRunner installRunner,
        IHarnessProcessManager processManager,
        IHarnessHealthMonitor healthMonitor,
        IRecentLogBuffer? recentLogs = null)
    {
        _discovery = discovery;
        _store = store;
        _installRunner = installRunner;
        _processManager = processManager;
        _healthMonitor = healthMonitor;
        _recentLogs = recentLogs;
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
            await VerifyCandidateAsync(candidate, cancellationToken);
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

    private async Task VerifyCandidateAsync(
        DshInstallationCandidate candidate,
        CancellationToken cancellationToken)
    {
        var port = ReserveLoopbackPort();
        var uri = new Uri($"http://127.0.0.1:{port}/");
        var smokeRoot = Path.Combine(
            Path.GetTempPath(),
            "DeepSeekHarnessDesktop",
            "dsh-smoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(smokeRoot);
        try
        {
            var options = new DshLaunchOptions
            {
                ExecutablePath = candidate.ExecutablePath,
                Arguments =
                [
                    candidate.EntryPointPath
                        ?? throw new InvalidOperationException("Private DSH entry point is missing."),
                    "web",
                    "--port",
                    port.ToString(CultureInfo.InvariantCulture),
                ],
                WorkingDirectory = smokeRoot,
                FallbackUri = uri,
                StartupTimeout = SmokeTimeout,
                Environment = new Dictionary<string, string>
                {
                    ["DSH_HOME"] = Path.Combine(smokeRoot, "home"),
                    ["DSH_DESKTOP_HOST"] = "1",
                    ["DSH_DESKTOP_VERSION"] = GetDesktopVersion(),
                },
            };
            await _processManager.StartAsync(options, cancellationToken);
            var result = await _healthMonitor.WaitUntilReadyAsync(
                () => uri,
                SmokeTimeout,
                cancellationToken);
            if (result.Status != HealthProbeStatus.DshConfirmed || !_processManager.IsRunning)
            {
                throw new HarnessException(new HarnessError(
                    "DSH-E201",
                    "DSH 安装后验证失败",
                    result.Detail ?? "The private DSH smoke process did not become ready.",
                    true));
            }
        }
        finally
        {
            await _processManager.StopAsync(CancellationToken.None);
            await DeleteSmokeRootAsync(smokeRoot);
        }
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private async Task DeleteSmokeRootAsync(string smokeRoot)
    {
        var expectedParent = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "DeepSeekHarnessDesktop",
            "dsh-smoke"));
        var actualParent = Path.GetFullPath(Path.GetDirectoryName(smokeRoot)!);
        if (!string.Equals(expectedParent, actualParent, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Exception? lastError = null;
        foreach (var delay in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1) })
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }
            try
            {
                if (!Directory.Exists(smokeRoot))
                {
                    return;
                }
                ClearReadOnlyFiles(smokeRoot);
                Directory.Delete(smokeRoot, true);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
            }
        }

        if (lastError is not null)
        {
            _recentLogs?.AddDesktop($"DSH smoke 暂存目录清理失败：{lastError.GetType().Name}。");
        }
    }

    private static void ClearReadOnlyFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string GetDesktopVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
}
