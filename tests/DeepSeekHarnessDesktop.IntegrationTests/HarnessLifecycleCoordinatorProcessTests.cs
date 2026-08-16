using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.TestHarness;

namespace DeepSeekHarnessDesktop.IntegrationTests;

public sealed class HarnessLifecycleCoordinatorProcessTests
{
    [Fact]
    public async Task ImmediateProcessExitFailsWithExitCodeAndCapturedStderr()
    {
        var logs = new RecentLogBuffer();
        await using var manager = new HarnessProcessManager(logs);
        await using var coordinator = await CreateCoordinatorAsync(manager, "--exit", WaitMode.UntilCancelled);

        await coordinator.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HarnessRuntimeState.Failed, coordinator.Current.State);
        Assert.Equal("DSH-E201", coordinator.Current.Error?.Code);
        Assert.Contains("code 23", coordinator.Current.Error?.TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains(logs.Snapshot(), line =>
            line.Source == ProcessOutputSource.StandardError
            && line.Text.Contains("fixture immediate exit", StringComparison.Ordinal));
        Assert.False(manager.IsRunning);
    }

    [Fact]
    public async Task RuntimeProcessCrashMovesRunningOwnedToFailed()
    {
        var logs = new RecentLogBuffer();
        await using var manager = new HarnessProcessManager(logs);
        await using var coordinator = await CreateCoordinatorAsync(manager, "--crash", WaitMode.Ready);
        var failed = new TaskCompletionSource<HarnessStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, snapshot) =>
        {
            if (snapshot.State == HarnessRuntimeState.Failed)
            {
                failed.TrySetResult(snapshot);
            }
        };

        await coordinator.StartAsync(CancellationToken.None);
        Assert.Equal(HarnessRuntimeState.RunningOwned, coordinator.Current.State);
        var snapshot = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("DSH-E201", snapshot.Error?.Code);
        Assert.Contains("code 24", snapshot.Error?.TechnicalMessage, StringComparison.Ordinal);
        Assert.Contains(logs.Snapshot(), line =>
            line.Source == ProcessOutputSource.StandardError
            && line.Text.Contains("fixture runtime crash", StringComparison.Ordinal));
        Assert.False(manager.IsRunning);
    }

    [Fact]
    public async Task StartupTimeoutStopsOwnedProcessTreeAndReportsE203()
    {
        var logs = new RecentLogBuffer();
        var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        logs.LineAdded += (_, line) =>
        {
            const string prefix = "CHILD_PID=";
            if (line.Text.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(line.Text[prefix.Length..], out var pid))
            {
                childPid.TrySetResult(pid);
            }
        };
        await using var manager = new HarnessProcessManager(logs);
        await using var coordinator = await CreateCoordinatorAsync(
            manager,
            "--tree",
            WaitMode.Timeout,
            TimeSpan.FromMilliseconds(500));

        var start = coordinator.StartAsync(CancellationToken.None);
        var descendantPid = await childPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await start.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HarnessRuntimeState.Failed, coordinator.Current.State);
        Assert.Equal("DSH-E203", coordinator.Current.Error?.Code);
        Assert.False(manager.IsRunning);
        await AssertProcessExitedAsync(descendantPid);
    }

    [Fact]
    public async Task StopDuringStartupCancelsProbeAndStopsRealProcess()
    {
        var logs = new RecentLogBuffer();
        await using var manager = new HarnessProcessManager(logs);
        await using var coordinator = await CreateCoordinatorAsync(manager, "--emit", WaitMode.UntilCancelled);

        var start = coordinator.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => manager.IsRunning, TimeSpan.FromSeconds(5));
        await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await start.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HarnessRuntimeState.Stopped, coordinator.Current.State);
        Assert.False(manager.IsRunning);
    }

    private static async Task<HarnessLifecycleCoordinator> CreateCoordinatorAsync(
        HarnessProcessManager manager,
        string mode,
        WaitMode waitMode,
        TimeSpan? startupTimeout = null)
    {
        var settings = new AppSettings
        {
            WorkspacePath = Path.GetTempPath(),
            AutoStart = false,
        };
        var options = new DshLaunchOptions
        {
            ExecutablePath = DotnetPath(),
            Arguments = [typeof(HarnessMarker).Assembly.Location, mode],
            WorkingDirectory = settings.WorkspacePath,
            FallbackUri = settings.ServiceUri,
            StartupTimeout = startupTimeout ?? TimeSpan.FromSeconds(5),
        };
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(),
            new FixedResolver(options),
            manager,
            new ControlledHealthMonitor(waitMode),
            settings);
        await coordinator.InitializeAsync(CancellationToken.None);
        return coordinator;
    }

    private static string DotnetPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!predicate())
        {
            await Task.Delay(25, cts.Token);
        }
    }

    private static async Task AssertProcessExitedAsync(int processId)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }
            await Task.Delay(100);
        }

        Assert.Fail($"Descendant process {processId} is still running.");
    }

    private sealed class FixedResolver(DshLaunchOptions options) : IDshCommandResolver
    {
        public Task<DshLaunchOptions> ResolveAsync(AppSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(options);
    }

    private sealed class ControlledHealthMonitor(WaitMode mode) : IHarnessHealthMonitor
    {
        public Task<HealthProbeResult> ProbeAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new HealthProbeResult(HealthProbeStatus.Unreachable, uri));

        public async Task<HealthProbeResult> WaitUntilReadyAsync(
            Func<Uri> uriProvider,
            TimeSpan startupTimeout,
            CancellationToken cancellationToken)
        {
            if (mode == WaitMode.Ready)
            {
                var uri = uriProvider();
                return new HealthProbeResult(HealthProbeStatus.DshConfirmed, uri, uri);
            }

            if (mode == WaitMode.Timeout)
            {
                await Task.Delay(startupTimeout, cancellationToken);
                return new HealthProbeResult(HealthProbeStatus.Unreachable, uriProvider(), Detail: "Fixture startup timeout.");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after cancellation.");
        }
    }

    private enum WaitMode
    {
        Ready,
        Timeout,
        UntilCancelled,
    }
}
