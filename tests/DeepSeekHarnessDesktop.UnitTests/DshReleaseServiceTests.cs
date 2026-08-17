using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Utilities;
using System.Net;
using System.Text;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DshReleaseServiceTests
{
    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("0.1.0-rc.6", false)]
    [InlineData("0.1.0-rc.5", false)]
    public async Task ComparesPrereleaseVersions(string latest, bool expectedUpdate)
    {
        using var client = new HttpClient(new StubHandler(_ => Json($"{{\"version\":\"{latest}\"}}")));
        using var service = new DshReleaseService(client);

        var result = await service.CheckLatestAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedUpdate, result.IsUpdateAvailable);
        Assert.Equal(latest, result.LatestVersion);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{broken")]
    public async Task InvalidRegistryResponseIsIsolated(string body)
    {
        using var client = new HttpClient(new StubHandler(_ => Json(body)));
        using var service = new DshReleaseService(client);

        var result = await service.CheckLatestAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public async Task OversizedRegistryResponseIsRejected()
    {
        var body = new string('x', DshReleaseService.MaximumResponseBytes + 1);
        using var client = new HttpClient(new StubHandler(_ => Json(body)));
        using var service = new DshReleaseService(client);

        var result = await service.CheckLatestAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("64 KiB", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsesFixedOfficialRegistryEndpoint()
    {
        Uri? requestedUri = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return Json("{\"version\":\"0.1.0-rc.6\"}");
        }));
        using var service = new DshReleaseService(client);

        var result = await service.CheckLatestAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(DshPackageMetadata.NpmLatestUri, requestedUri);
    }

    [Fact]
    public async Task HttpErrorIsIsolated()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var service = new DshReleaseService(client);

        var result = await service.CheckLatestAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("检查更新失败", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpClientTimeoutReturnsFailure()
    {
        using var client = new HttpClient(new AsyncStubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return Json("{\"version\":\"0.1.0-rc.6\"}");
        }))
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        using var service = new DshReleaseService(client);

        var result = await service.CheckLatestAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("超时", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var client = new HttpClient(new AsyncStubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return Json("{\"version\":\"0.1.0-rc.6\"}");
        }));
        using var service = new DshReleaseService(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckLatestAsync(cancellation.Token));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class AsyncStubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
