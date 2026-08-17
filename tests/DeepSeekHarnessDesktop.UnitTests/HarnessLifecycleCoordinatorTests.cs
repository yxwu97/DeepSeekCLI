using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class HarnessLifecycleCoordinatorTests
{
    [Fact]
    public async Task StartCreatesOwnedProcessAfterUnreachablePreflight()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.ReadyResult = Confirmed();

        await fixture.Coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(HarnessRuntimeState.RunningOwned, fixture.Coordinator.Current.State);
        Assert.True(fixture.Coordinator.Current.IsOwned);
        Assert.Equal(1, fixture.Process.StartCount);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ConfirmedExternalServiceDoesNotCreateProcess()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.DshConfirmed);

        await fixture.Coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(HarnessRuntimeState.RunningExternal, fixture.Coordinator.Current.State);
        Assert.Equal(0, fixture.Process.StartCount);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task UnknownServiceFailsWithoutCreatingOrStoppingProcess()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.ReachableUnknown);

        await fixture.Coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(HarnessRuntimeState.Failed, fixture.Coordinator.Current.State);
        Assert.Equal("DSH-E205", fixture.Coordinator.Current.Error?.Code);
        Assert.Equal(0, fixture.Process.StartCount);
        Assert.Equal(0, fixture.Process.StopCount);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentStartRequestsCreateOneProcess()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.ReadyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Health.ReadyResult = Confirmed();

        var first = fixture.Coordinator.StartAsync(CancellationToken.None);
        await fixture.Process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = fixture.Coordinator.StartAsync(CancellationToken.None);
        fixture.Health.ReadyGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, fixture.Process.StartCount);
        Assert.Equal(HarnessRuntimeState.RunningOwned, fixture.Coordinator.Current.State);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task StopDuringStartCancelsProbeAndCleansProcess()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.BlockReadyUntilCancelled = true;

        var starting = fixture.Coordinator.StartAsync(CancellationToken.None);
        await fixture.Health.ReadyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Coordinator.StopAsync(CancellationToken.None);
        await starting;

        Assert.Equal(HarnessRuntimeState.Stopped, fixture.Coordinator.Current.State);
        Assert.Equal(1, fixture.Process.StartCount);
        Assert.Equal(1, fixture.Process.StopCount);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task NpxStartReportsAutomaticPreparationWhileWaiting()
    {
        var fixture = await CreateFixtureAsync(useNpx: true);
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.BlockReadyUntilCancelled = true;

        var starting = fixture.Coordinator.StartAsync(CancellationToken.None);
        await fixture.Health.ReadyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(HarnessRuntimeState.Starting, fixture.Coordinator.Current.State);
        Assert.Equal(
            "正在通过 npx 自动准备并启动 DSH，无需手动操作，最长等待 5 秒",
            fixture.Coordinator.Current.StatusMessage);

        await fixture.Coordinator.StopAsync(CancellationToken.None);
        await starting;
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task StopDuringAutomaticInitializationDoesNotEscapeCancellation()
    {
        var logs = new RecentLogBuffer();
        var process = new FakeProcessManager(logs);
        var health = new FakeHealthMonitor { BlockReadyUntilCancelled = true };
        health.EnqueueProbe(HealthProbeStatus.Unreachable);
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), AutoStart = true };
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(),
            new FakeResolver(settings, useNpx: true),
            process,
            health,
            settings,
            recentLogs: logs);

        var initializing = coordinator.InitializeAsync(CancellationToken.None);
        await health.ReadyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.StopAsync(CancellationToken.None);
        await initializing;

        Assert.Equal(HarnessRuntimeState.Stopped, coordinator.Current.State);
        Assert.Equal(1, process.StopCount);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task ReadyResultReturnedAfterCancellationCannotOverwriteStoppedState()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.ReadyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Health.IgnoreCancellation = true;

        var starting = fixture.Coordinator.StartAsync(CancellationToken.None);
        await fixture.Process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopping = fixture.Coordinator.StopAsync(CancellationToken.None);
        fixture.Health.ReadyGate.SetResult();
        await Task.WhenAll(starting, stopping);

        Assert.Equal(HarnessRuntimeState.Stopped, fixture.Coordinator.Current.State);
        Assert.False(fixture.Coordinator.Current.IsOwned);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task RestartWaitsForTwoUnreachableProbesBeforeSecondStart()
    {
        var fixture = await CreateRunningFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.ReadyResult = Confirmed(new Uri("http://127.0.0.1:43124/"));

        await fixture.Coordinator.RestartAsync(CancellationToken.None);

        Assert.Equal(2, fixture.Process.StartCount);
        Assert.Equal(1, fixture.Process.StopCount);
        Assert.Equal(4, fixture.Health.ProbeCount);
        Assert.Equal(new Uri("http://127.0.0.1:43124/"), fixture.Coordinator.Current.ServiceUri);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task RestartDoesNotStartWhenOldEndpointIsOccupied()
    {
        var fixture = await CreateRunningFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.ReachableUnknown);

        await fixture.Coordinator.RestartAsync(CancellationToken.None);

        Assert.Equal(HarnessRuntimeState.Failed, fixture.Coordinator.Current.State);
        Assert.Equal("DSH-E205", fixture.Coordinator.Current.Error?.Code);
        Assert.Equal(1, fixture.Process.StartCount);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ExternalHealthLossMovesToStoppedWithoutStartingProcess()
    {
        var process = new FakeProcessManager();
        var health = new FakeHealthMonitor();
        health.EnqueueProbe(HealthProbeStatus.DshConfirmed);
        var watcher = new ControlledRuntimeWatcher();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), AutoStart = false };
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(),
            new FakeResolver(settings),
            process,
            health,
            settings,
            watcher);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, snapshot) =>
        {
            if (snapshot.State == HarnessRuntimeState.Stopped)
            {
                stopped.TrySetResult();
            }
        };

        await coordinator.InitializeAsync(CancellationToken.None);
        Assert.Equal(HarnessRuntimeState.RunningExternal, coordinator.Current.State);
        watcher.LoseHealth();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(HarnessRuntimeState.Stopped, coordinator.Current.State);
        Assert.Equal(0, process.StartCount);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task ExitRaisedBeforeStartReturnsFailsWithProcessExitError()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.BlockReadyUntilCancelled = true;
        fixture.Process.ExitDuringStartCode = 23;

        await fixture.Coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(HarnessRuntimeState.Failed, fixture.Coordinator.Current.State);
        Assert.Equal("DSH-E201", fixture.Coordinator.Current.Error?.Code);
        Assert.Contains("code 23", fixture.Coordinator.Current.Error?.TechnicalMessage, StringComparison.Ordinal);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task NpxDnsFailureUsesActionableErrorCode()
    {
        var fixture = await CreateFixtureAsync(useNpx: true);
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.BlockReadyUntilCancelled = true;
        fixture.Process.ErrorDuringStartLine = "npm ERR! code ENOTFOUND";
        fixture.Process.ExitDuringStartCode = 1;

        await fixture.Coordinator.StartAsync(CancellationToken.None);

        Assert.Equal(HarnessRuntimeState.Failed, fixture.Coordinator.Current.State);
        Assert.Equal("DSH-E211", fixture.Coordinator.Current.Error?.Code);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task StoppedServiceAddressApplyPersistsWithoutStartingProcess()
    {
        var process = new FakeProcessManager();
        var health = new FakeHealthMonitor();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), AutoStart = false };
        var store = new FakeSettingsService();
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(), new FakeResolver(settings), process, health, settings, settingsService: store);
        await coordinator.InitializeAsync(CancellationToken.None);
        var target = new Uri("http://127.0.0.1:43130/");

        await coordinator.ApplyServiceUriAsync(target, CancellationToken.None);

        Assert.Equal(target, settings.ServiceUri);
        Assert.Equal(target, store.SavedUri);
        Assert.Equal(0, process.StartCount);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task ExternalAddressSwitchConfirmsBeforeSavingAndReplacingWatcher()
    {
        var process = new FakeProcessManager();
        var health = new FakeHealthMonitor();
        health.EnqueueProbe(HealthProbeStatus.DshConfirmed);
        var watcher = new RecordingRuntimeWatcher();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), AutoStart = false };
        var store = new FakeSettingsService();
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(), new FakeResolver(settings), process, health, settings, watcher, store);
        await coordinator.InitializeAsync(CancellationToken.None);
        var target = new Uri("http://127.0.0.1:43131/");
        health.EnqueueProbe(HealthProbeStatus.DshConfirmed);

        await coordinator.ApplyServiceUriAsync(target, CancellationToken.None);

        Assert.Equal(HarnessRuntimeState.RunningExternal, coordinator.Current.State);
        Assert.Equal(target, coordinator.Current.ServiceUri);
        Assert.Equal(target, store.SavedUri);
        Assert.Equal(2, watcher.CallCount);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task FailedExternalAddressSwitchKeepsOriginalSettingsAndWatcher()
    {
        var process = new FakeProcessManager();
        var health = new FakeHealthMonitor();
        health.EnqueueProbe(HealthProbeStatus.DshConfirmed);
        var watcher = new RecordingRuntimeWatcher();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), AutoStart = false };
        var original = settings.ServiceUri;
        var store = new FakeSettingsService();
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(), new FakeResolver(settings), process, health, settings, watcher, store);
        await coordinator.InitializeAsync(CancellationToken.None);
        health.EnqueueProbe(HealthProbeStatus.ReachableUnknown);

        var exception = await Assert.ThrowsAsync<HarnessException>(() => coordinator.ApplyServiceUriAsync(
            new Uri("http://127.0.0.1:43132/"), CancellationToken.None));

        Assert.Equal("DSH-E205", exception.Error.Code);
        Assert.Equal(original, settings.ServiceUri);
        Assert.Equal(original, coordinator.Current.ServiceUri);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(2, watcher.CallCount);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task OwnedAddressApplySavesThenUsesSerializedRestart()
    {
        var process = new FakeProcessManager();
        var health = new FakeHealthMonitor();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), AutoStart = false };
        var store = new FakeSettingsService();
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(), new FakeResolver(settings), process, health, settings, settingsService: store);
        await coordinator.InitializeAsync(CancellationToken.None);
        health.EnqueueProbe(HealthProbeStatus.Unreachable);
        health.ReadyResult = Confirmed();
        await coordinator.StartAsync(CancellationToken.None);
        var target = new Uri("http://127.0.0.1:43133/");
        health.EnqueueProbe(HealthProbeStatus.Unreachable);
        health.EnqueueProbe(HealthProbeStatus.Unreachable);
        health.ReadyResult = Confirmed(target);

        await coordinator.ApplyServiceUriAsync(target, CancellationToken.None);

        Assert.Equal(HarnessRuntimeState.RunningOwned, coordinator.Current.State);
        Assert.Equal(target, coordinator.Current.ServiceUri);
        Assert.Equal(target, store.SavedUri);
        Assert.Equal(2, process.StartCount);
        Assert.Equal(1, process.StopCount);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task CancellingOwnedAddressApplyDoesNotLeaveRestartingState()
    {
        var process = new FakeProcessManager();
        var health = new FakeHealthMonitor();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), AutoStart = false };
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(), new FakeResolver(settings), process, health, settings, settingsService: new FakeSettingsService());
        await coordinator.InitializeAsync(CancellationToken.None);
        health.EnqueueProbe(HealthProbeStatus.Unreachable);
        health.ReadyResult = Confirmed();
        await coordinator.StartAsync(CancellationToken.None);
        health.BlockProbeUntilCancelled = true;
        using var cancellation = new CancellationTokenSource();

        var apply = coordinator.ApplyServiceUriAsync(new Uri("http://127.0.0.1:43134/"), cancellation.Token);
        await health.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => apply);
        Assert.Equal(HarnessRuntimeState.Stopped, coordinator.Current.State);
        Assert.False(coordinator.Current.IsOwned);
        await coordinator.DisposeAsync();
    }

    private static async Task<Fixture> CreateFixtureAsync(bool useNpx = false)
    {
        var logs = new RecentLogBuffer();
        var process = new FakeProcessManager(logs);
        var health = new FakeHealthMonitor();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), AutoStart = false };
        var coordinator = new HarnessLifecycleCoordinator(
            new HarnessStateMachine(),
            new FakeResolver(settings, useNpx),
            process,
            health,
            settings,
            recentLogs: logs);
        await coordinator.InitializeAsync(CancellationToken.None);
        return new Fixture(coordinator, process, health);
    }

    private static async Task<Fixture> CreateRunningFixtureAsync()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Health.EnqueueProbe(HealthProbeStatus.Unreachable);
        fixture.Health.ReadyResult = Confirmed();
        await fixture.Coordinator.StartAsync(CancellationToken.None);
        return fixture;
    }

    private static HealthProbeResult Confirmed(Uri? uri = null)
    {
        uri ??= new Uri("http://127.0.0.1:43123/");
        return new HealthProbeResult(HealthProbeStatus.DshConfirmed, uri, uri);
    }

    private sealed record Fixture(
        HarnessLifecycleCoordinator Coordinator,
        FakeProcessManager Process,
        FakeHealthMonitor Health) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Coordinator.DisposeAsync();
    }

    private sealed class FakeResolver(AppSettings settings, bool useNpx = false) : IDshCommandResolver
    {
        public Task<DshLaunchOptions> ResolveAsync(AppSettings _, CancellationToken cancellationToken) =>
            Task.FromResult(new DshLaunchOptions
            {
                ExecutablePath = useNpx
                    ? Path.Combine(Path.GetTempPath(), "npx.cmd")
                    : Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = [],
                WorkingDirectory = settings.WorkspacePath,
                FallbackUri = settings.ServiceUri,
                StartupTimeout = TimeSpan.FromSeconds(5),
            });
    }

    private sealed class FakeProcessManager(IRecentLogBuffer? logs = null) : IHarnessProcessManager
    {
        private int _nextProcessId = 100;
        public event EventHandler<ProcessOutputEventArgs>? OutputReceived;
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited;
        public HarnessProcessInfo? Current { get; private set; }
        public bool IsRunning => Current is not null;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int? ExitDuringStartCode { get; set; }
        public string? ErrorDuringStartLine { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HarnessProcessInfo> StartAsync(DshLaunchOptions options, CancellationToken cancellationToken)
        {
            StartCount++;
            var process = new HarnessProcessInfo(++_nextProcessId, DateTimeOffset.UtcNow, options.WorkingDirectory, null);
            Current = process;
            Started.TrySetResult();
            OutputReceived?.Invoke(this, new ProcessOutputEventArgs(new ProcessOutputLine(
                DateTimeOffset.UtcNow, ProcessOutputSource.StandardOutput, "ready http://127.0.0.1:43123/")));
            if (ErrorDuringStartLine is { } errorText)
            {
                var errorLine = new ProcessOutputLine(
                    DateTimeOffset.UtcNow,
                    ProcessOutputSource.StandardError,
                    errorText);
                logs?.Add(errorLine);
                OutputReceived?.Invoke(this, new ProcessOutputEventArgs(errorLine));
            }
            if (ExitDuringStartCode is { } exitCode)
            {
                Current = null;
                ProcessExited?.Invoke(this, new ProcessExitedEventArgs(process.ProcessId, exitCode));
            }
            return Task.FromResult(process);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (Current is { } current)
            {
                StopCount++;
                Current = null;
                ProcessExited?.Invoke(this, new ProcessExitedEventArgs(current.ProcessId, 0));
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHealthMonitor : IHarnessHealthMonitor
    {
        private readonly Queue<HealthProbeStatus> _probes = new();
        public int ProbeCount { get; private set; }
        public HealthProbeResult ReadyResult { get; set; } = Confirmed();
        public TaskCompletionSource? ReadyGate { get; set; }
        public bool BlockReadyUntilCancelled { get; set; }
        public bool IgnoreCancellation { get; set; }
        public bool BlockProbeUntilCancelled { get; set; }
        public TaskCompletionSource ProbeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void EnqueueProbe(HealthProbeStatus status) => _probes.Enqueue(status);

        public async Task<HealthProbeResult> ProbeAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ProbeCount++;
            if (BlockProbeUntilCancelled)
            {
                ProbeStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            var status = _probes.Count > 0 ? _probes.Dequeue() : HealthProbeStatus.Unreachable;
            return new HealthProbeResult(status, uri, status == HealthProbeStatus.DshConfirmed ? uri : null);
        }

        public async Task<HealthProbeResult> WaitUntilReadyAsync(
            Func<Uri> uriProvider,
            TimeSpan startupTimeout,
            CancellationToken cancellationToken)
        {
            ReadyStarted.TrySetResult();
            if (BlockReadyUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (ReadyGate is not null)
            {
                if (IgnoreCancellation)
                {
                    await ReadyGate.Task;
                }
                else
                {
                    await ReadyGate.Task.WaitAsync(cancellationToken);
                }
            }
            return ReadyResult;
        }
    }

    private sealed class ControlledRuntimeWatcher : IRuntimeHealthWatcher
    {
        private readonly TaskCompletionSource<RuntimeHealthLost?> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Uri? _uri;
        private long _generation;

        public Task<RuntimeHealthLost?> WatchAsync(Uri uri, long generation, CancellationToken cancellationToken)
        {
            _uri = uri;
            _generation = generation;
            return _result.Task.WaitAsync(cancellationToken);
        }

        public void LoseHealth()
        {
            var uri = _uri ?? throw new InvalidOperationException("Watcher has not started.");
            _result.TrySetResult(new RuntimeHealthLost(
                _generation,
                new HealthProbeResult(HealthProbeStatus.Unreachable, uri)));
        }
    }

    private sealed class RecordingRuntimeWatcher : IRuntimeHealthWatcher
    {
        public int CallCount { get; private set; }

        public async Task<RuntimeHealthLost?> WatchAsync(Uri uri, long generation, CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public int SaveCount { get; private set; }
        public Uri? SavedUri { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            SaveCount++;
            SavedUri = settings.ServiceUri;
            return Task.CompletedTask;
        }
    }
}
