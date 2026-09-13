using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;

namespace DeepSeekHarnessDesktop.IntegrationTests;

public sealed class HarnessHealthMonitorTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task AuthenticationChallengeIsNotAPortConflictOrConfirmedDsh(int status)
    {
        await using var server = new FakeHarnessServer(_ => new FakeResponse(StatusCode: status));
        using var monitor = new HarnessHealthMonitor();
        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(HealthProbeStatus.AuthenticationRequired, result.Status);
    }

    [Fact]
    public void DefaultLoopbackHandlerDoesNotUseSystemProxy()
    {
        using var handler = HarnessHealthMonitor.CreateLoopbackHandler();

        Assert.False(handler.UseProxy);
    }

    [Fact]
    public async Task ConfirmsDshOnlyWhenBothFeaturesExist()
    {
        await using var server = new FakeHarnessServer(_ => new FakeResponse());
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.DshConfirmed, result.Status);
        Assert.Equal(server.BaseUri, result.FinalUri);
    }

    [Fact]
    public async Task ConfirmsRc2GlobalThisBootMarker()
    {
        const string body =
            "<title>DeepSeek Harness</title><script>globalThis[\"__DSH_BOOT__\"]={};</script>";
        await using var server = new FakeHarnessServer(_ => new FakeResponse(Body: body));
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.ProbeAsync(
            server.BaseUri,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.Equal(HealthProbeStatus.DshConfirmed, result.Status);
    }

    [Theory]
    [InlineData("<title>DeepSeek Harness</title>")]
    [InlineData("<script>window.__DSH_BOOT__={};</script>")]
    [InlineData("<title>DeepSeek Harness</title><script>globalThis.__DSH_BOOT__={};</script>")]
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

    [Theory]
    [InlineData(404)]
    [InlineData(401)]
    public async Task WaitUntilReadyRetriesStartupResponseUntilIdentityIsConfirmed(int statusCode)
    {
        var requests = 0;
        await using var server = new FakeHarnessServer(_ => Interlocked.Increment(ref requests) == 1
            ? new FakeResponse(StatusCode: statusCode, Body: string.Empty)
            : new FakeResponse());
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.WaitUntilReadyAsync(
            () => server.BaseUri, TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(HealthProbeStatus.DshConfirmed, result.Status);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(401)]
    public async Task PersistentStartupResponseRemainsUnknown(int statusCode)
    {
        var requests = 0;
        await using var server = new FakeHarnessServer(_ =>
        {
            Interlocked.Increment(ref requests);
            return new FakeResponse(StatusCode: statusCode);
        });
        using var monitor = new HarnessHealthMonitor();

        var result = await monitor.WaitUntilReadyAsync(
            () => server.BaseUri, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(statusCode == 401 ? HealthProbeStatus.AuthenticationRequired : HealthProbeStatus.ReachableUnknown, result.Status);
        Assert.True(requests > 1);
    }

    [Fact]
    public async Task StartupResponseRetryCanBeCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var requests = 0;
        await using var server = new FakeHarnessServer(_ =>
        {
            if (Interlocked.Increment(ref requests) == 2) cancellation.Cancel();
            return new FakeResponse(StatusCode: 404);
        });
        using var monitor = new HarnessHealthMonitor();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.WaitUntilReadyAsync(
            () => server.BaseUri, TimeSpan.FromSeconds(3), cancellation.Token));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task AuthenticationRequiresCookieExchangeAndHtmlIdentity(bool validIdentity, bool useLocalhost, bool externalLink)
    {
        var cookieSeen = false;
        await using var server = new FakeHarnessServer(_ => new FakeResponse())
        {
            RequestHandler = (path, headers) =>
            {
                if (path.StartsWith("/?token=", StringComparison.Ordinal))
                    return new FakeResponse(StatusCode: 303, Location: "/", SetCookie: "dsh-auth-test=signed-fixture; Path=/; HttpOnly; SameSite=Strict");
                cookieSeen = headers.TryGetValue("Cookie", out var cookie) && cookie.Contains("signed-fixture", StringComparison.Ordinal);
                return cookieSeen
                    ? validIdentity ? new FakeResponse() : new FakeResponse(Body: "ordinary page")
                    : new FakeResponse(StatusCode: 401);
            },
        };
        var configured = useLocalhost ? new UriBuilder(server.BaseUri) { Host = "localhost" }.Uri : server.BaseUri;
        var session = new DshBrowserSession();
        if (externalLink)
            Assert.True(session.TryBeginExternal(configured, $"{server.BaseUri}?token={new string('a', 43)}"));
        else
        {
            session.Begin(configured);
            session.CaptureOutput($"dsh web: {server.BaseUri}?token={new string('a', 43)}");
        }
        using var monitor = new HarnessHealthMonitor(session);

        var result = await monitor.ProbeAsync(configured, TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.True(cookieSeen);
        Assert.Equal(validIdentity ? HealthProbeStatus.DshConfirmed : HealthProbeStatus.ReachableUnknown, result.Status);
        Assert.Equal(configured, result.RequestedUri);
        Assert.Equal(server.BaseUri, result.FinalUri);
        Assert.DoesNotContain("token=", result.ToString(), StringComparison.Ordinal);
        session.Clear();
        var external = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(3), CancellationToken.None);
        Assert.Equal(HealthProbeStatus.AuthenticationRequired, external.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationNeverFollowsForeignOriginRedirect(bool afterExchange)
    {
        var requests = 0;
        await using var foreign = new FakeHarnessServer(_ =>
        {
            Interlocked.Increment(ref requests);
            return new FakeResponse();
        });
        await using var server = new FakeHarnessServer(path => new FakeResponse(
            StatusCode: 303, Location: afterExchange && path.StartsWith("/?token=", StringComparison.Ordinal)
                ? "/" : foreign.BaseUri.AbsoluteUri,
            SetCookie: "dsh-auth-test=signed-fixture; Path=/"));
        using var monitor = new HarnessHealthMonitor(CreateBrowserSession(server.BaseUri));

        var result = await monitor.ProbeAsync(server.BaseUri, TimeSpan.FromSeconds(3), CancellationToken.None);

        Assert.Equal(afterExchange ? HealthProbeStatus.ExternalRedirect : HealthProbeStatus.ReachableUnknown, result.Status);
        Assert.Equal(0, requests);
    }

    private static DshBrowserSession CreateBrowserSession(Uri origin)
    {
        var session = new DshBrowserSession();
        session.Begin(origin);
        session.CaptureOutput($"dsh web: {origin}?token={new string('a', 43)}");
        return session;
    }
}
