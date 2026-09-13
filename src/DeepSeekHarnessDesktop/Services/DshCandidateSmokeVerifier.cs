using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCandidateSmokeVerifier : IDshCandidateSmokeVerifier
{
    // Cold module loading on Windows can exceed 30 seconds even for a healthy DSH.
    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromMinutes(2);
    private readonly IHarnessProcessManager _processManager;
    private readonly IHarnessHealthMonitor _healthMonitor;
    private readonly IRecentLogBuffer? _recentLogs;

    public DshCandidateSmokeVerifier(
        IHarnessProcessManager processManager,
        IHarnessHealthMonitor healthMonitor,
        IRecentLogBuffer? recentLogs = null)
    {
        _processManager = processManager;
        _healthMonitor = healthMonitor;
        _recentLogs = recentLogs;
    }

    public async Task VerifyAsync(
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
                    "--no-open",
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
                throw new HarnessException(DshUpdateErrorMapper.SmokeFailed(
                    result.Detail ?? "The private DSH smoke process did not become ready."));
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
