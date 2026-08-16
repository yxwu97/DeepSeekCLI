using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class RuntimeHealthWatcherTests
{
    private static readonly Uri ServiceUri = new("http://127.0.0.1:3080/");

    [Fact]
    public async Task ThreeConsecutiveUnreachableResultsLoseHealth()
    {
        var monitor = new SequenceHealthMonitor(
            HealthProbeStatus.Unreachable,
            HealthProbeStatus.Unreachable,
            HealthProbeStatus.Unreachable);
        var watcher = new RuntimeHealthWatcher(monitor, TimeSpan.FromMilliseconds(1));

        var lost = await watcher.WatchAsync(ServiceUri, 7, CancellationToken.None);

        Assert.NotNull(lost);
        Assert.Equal(7, lost.Generation);
        Assert.Equal(3, monitor.CallCount);
    }

    [Fact]
    public async Task SuccessfulProbeResetsUnreachableCount()
    {
        var monitor = new SequenceHealthMonitor(
            HealthProbeStatus.Unreachable,
            HealthProbeStatus.Unreachable,
            HealthProbeStatus.DshConfirmed,
            HealthProbeStatus.Unreachable,
            HealthProbeStatus.Unreachable,
            HealthProbeStatus.Unreachable);
        var watcher = new RuntimeHealthWatcher(monitor, TimeSpan.FromMilliseconds(1));

        var lost = await watcher.WatchAsync(ServiceUri, 1, CancellationToken.None);

        Assert.NotNull(lost);
        Assert.Equal(6, monitor.CallCount);
    }

    [Fact]
    public async Task UnknownServiceLosesHealthImmediatelyWithE205()
    {
        var monitor = new SequenceHealthMonitor(HealthProbeStatus.ReachableUnknown);
        var watcher = new RuntimeHealthWatcher(monitor, TimeSpan.FromMilliseconds(1));

        var lost = await watcher.WatchAsync(ServiceUri, 1, CancellationToken.None);

        Assert.Equal("DSH-E205", lost?.Error?.Code);
        Assert.Equal(1, monitor.CallCount);
    }

    private sealed class SequenceHealthMonitor(params HealthProbeStatus[] statuses) : IHarnessHealthMonitor
    {
        private readonly Queue<HealthProbeStatus> _statuses = new(statuses);
        public int CallCount { get; private set; }

        public Task<HealthProbeResult> ProbeAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
        {
            CallCount++;
            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HealthProbeStatus.DshConfirmed;
            return Task.FromResult(new HealthProbeResult(status, uri, status == HealthProbeStatus.DshConfirmed ? uri : null));
        }

        public Task<HealthProbeResult> WaitUntilReadyAsync(Func<Uri> uriProvider, TimeSpan startupTimeout, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
