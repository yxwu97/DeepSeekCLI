using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;

namespace DeepSeekHarnessDesktop.IntegrationTests;

public sealed class HarnessHealthMonitorTests
{
    [Fact]
    public async Task ConfirmsDshOnlyWhenBothFeaturesExist()
    {
        await using var server = new FakeHarnessServer(_ => new FakeResponse());
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.DshConfirmed, result.Status);
        Assert.Equal(server.BaseUri, result.FinalUri);
    }

    [Theory]
    [InlineData("<title>DeepSeek Harness</title>")]
    [InlineData("<script>window.__DSH_BOOT__={};</script>")]
    [InlineData("ordinary page")]
    public async Task ClassifiesMissingIdentityFeatureAsUnknown(string body)
    {
        await using var server = new FakeHarnessServer(_ => new FakeResponse(Body: body));
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.ReachableUnknown, result.Status);
    }

    [Fact]
    public async Task FollowsLoopbackRedirectAndReturnsFinalUri()
    {
        await using var server = new FakeHarnessServer(path => path == "/"
            ? new FakeResponse(StatusCode: 302, Body: string.Empty, Location: "/dsh")
            : new FakeResponse());
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.DshConfirmed, result.Status);
        Assert.Equal(new Uri(server.BaseUri, "/dsh"), result.FinalUri);
    }

    [Fact]
    public async Task RejectsExternalRedirect()
    {
        await using var server = new FakeHarnessServer(_ => new FakeResponse(
            StatusCode: 302,
            Body: string.Empty,
            Location: "https://example.com/"));
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.ExternalRedirect, result.Status);
    }

    [Fact]
    public async Task RejectsRedirectLoop()
    {
        await using var server = new FakeHarnessServer(_ => new FakeResponse(StatusCode: 302, Body: string.Empty, Location: "/"));
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.InvalidUri, result.Status);
    }

    [Fact]
    public async Task BoundsHtmlResponseTo256KiB()
    {
        await using var server = new FakeHarnessServer(_ => new FakeResponse(Body: new string('x', HarnessHealthMonitor.MaximumResponseBytes + 1)));
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.ReachableUnknown, result.Status);
        Assert.Contains("256 KiB", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsUnreachableAfterServerStops()
    {
        var server = new FakeHarnessServer(_ => new FakeResponse());
        var uri = server.BaseUri;
        await server.DisposeAsync();
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(uri, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.Unreachable, result.Status);
    }

    [Fact]
    public async Task WaitUntilReadyUsesUpdatedCandidateUri()
    {
        await using var server = new FakeHarnessServer(_ => new FakeResponse());
        using var monitor = new HarnessHealthMonitor();
        var candidate = new Uri("http://127.0.0.1:1/");
        var update = Task.Run(async () =>
        {
            await Task.Delay(400);
            candidate = server.BaseUri;
        });

        var result = await monitor.WaitUntilReadyAsync(
            () => candidate,
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        await update;

        Assert.Equal(HealthProbeStatus.DshConfirmed, result.Status);
        Assert.Equal(server.BaseUri, result.FinalUri);
    }
}
