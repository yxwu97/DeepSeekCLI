using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DshPreparationServiceTests
{
    [Fact]
    public async Task ExistingCandidateSkipsInstallAndStoreTransaction()
    {
        var calls = new List<string>();
        var candidate = PrivateCandidate();
        var service = CreateService(calls, candidate, out var store, out var runner);

        await service.PrepareAsync(Settings(), CancellationToken.None);

        Assert.Empty(calls);
        Assert.Equal(0, store.CreateCount);
        Assert.Equal(0, runner.RunCount);
    }

    [Fact]
    public async Task FirstPreparationActivatesOnlyAfterConfirmedSmoke()
    {
        var calls = new List<string>();
        var service = CreateService(calls, null, out _, out _);

        await service.PrepareAsync(Settings(), CancellationToken.None);

        Assert.Equal(
            ["create", "install", "commit", "start", "health", "stop", "activate", "cleanup"],
            calls);
    }

    [Fact]
    public async Task FailedSmokeNeverActivatesCandidateAndStillCleansTransaction()
    {
        var calls = new List<string>();
        var service = CreateService(
            calls,
            null,
            out _,
            out _,
            HealthProbeStatus.Unreachable);

        var exception = await Assert.ThrowsAsync<HarnessException>(
            () => service.PrepareAsync(Settings(), CancellationToken.None));

        Assert.Equal("DSH-E201", exception.Error.Code);
        Assert.DoesNotContain("activate", calls);
        Assert.Equal("cleanup", calls[calls.Count - 1]);
    }

    private static DshPreparationService CreateService(
        List<string> calls,
        DshInstallationCandidate? existing,
        out FakeStore store,
        out FakeRunner runner,
        HealthProbeStatus smokeStatus = HealthProbeStatus.DshConfirmed)
    {
        var discovery = new FakeDiscovery(existing);
        store = new FakeStore(calls);
        runner = new FakeRunner(calls);
        return new DshPreparationService(
            discovery,
            store,
            runner,
            new FakeProcessManager(calls),
            new FakeHealthMonitor(calls, smokeStatus));
    }

    private static AppSettings Settings() => new()
    {
        WorkspacePath = Path.GetTempPath(),
        Launch = new LaunchSettings { Mode = LaunchMode.Auto },
    };

    private static DshInstallationCandidate PrivateCandidate() => new(
        DshInstallationSource.Private,
        Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        Path.Combine(Path.GetTempPath(), "dsh-test-bin.js"),
        DshPackageMetadata.ValidatedVersion,
        "private-test");

    private sealed class FakeDiscovery(DshInstallationCandidate? candidate)
        : IDshCandidateDiscoveryService
    {
        public Task<DshDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DshDiscoveryResult(
                candidate,
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Path.Combine(Environment.SystemDirectory, "npm.cmd"),
                null));
    }

    private sealed class FakeStore(List<string> calls) : IPrivateDshInstallationStore
    {
        public int CreateCount { get; private set; }

        public Task<DshInstallationCandidate?> FindActiveAsync(
            string? nodePath,
            CancellationToken cancellationToken) => Task.FromResult<DshInstallationCandidate?>(null);

        public Task<PrivateDshInstallTransaction> CreateTransactionAsync(CancellationToken cancellationToken)
        {
            CreateCount++;
            calls.Add("create");
            return Task.FromResult(new PrivateDshInstallTransaction(
                Path.Combine(Path.GetTempPath(), "dsh-preparation-test"),
                "private-test",
                new string('0', 64)));
        }

        public Task<DshInstallationCandidate> CommitVersionAsync(
            PrivateDshInstallTransaction transaction,
            string nodePath,
            CancellationToken cancellationToken)
        {
            calls.Add("commit");
            return Task.FromResult(PrivateCandidate());
        }

        public Task ActivateAsync(
            DshInstallationCandidate candidate,
            CancellationToken cancellationToken)
        {
            calls.Add("activate");
            return Task.CompletedTask;
        }

        public Task CleanupAsync(PrivateDshInstallTransaction transaction)
        {
            calls.Add("cleanup");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRunner(List<string> calls) : INpmInstallRunner
    {
        public int RunCount { get; private set; }

        public Task RunAsync(string npmPath, string workingDirectory, CancellationToken cancellationToken)
        {
            RunCount++;
            calls.Add("install");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessManager(List<string> calls) : IHarnessProcessManager
    {
        public event EventHandler<ProcessOutputEventArgs>? OutputReceived { add { } remove { } }
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited { add { } remove { } }
        public HarnessProcessInfo? Current { get; private set; }
        public bool IsRunning => Current is not null;

        public Task<HarnessProcessInfo> StartAsync(
            DshLaunchOptions options,
            CancellationToken cancellationToken)
        {
            calls.Add("start");
            Current = new HarnessProcessInfo(42, DateTimeOffset.UtcNow, options.WorkingDirectory, null);
            return Task.FromResult(Current);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            calls.Add("stop");
            Current = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new();
    }

    private sealed class FakeHealthMonitor(
        List<string> calls,
        HealthProbeStatus status) : IHarnessHealthMonitor
    {
        public Task<HealthProbeResult> ProbeAsync(
            Uri uri,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<HealthProbeResult> WaitUntilReadyAsync(
            Func<Uri> uriProvider,
            TimeSpan startupTimeout,
            CancellationToken cancellationToken)
        {
            calls.Add("health");
            var uri = uriProvider();
            return Task.FromResult(new HealthProbeResult(
                status,
                uri,
                status == HealthProbeStatus.DshConfirmed ? uri : null));
        }
    }
}
