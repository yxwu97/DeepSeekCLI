using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.ViewModels;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DeepSeekAccountServiceTests
{
    [Fact]
    public async Task BalanceRequestUsesOnlyOfficialEndpointAndBearerAuthentication()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            """
            {
              "is_available": true,
              "balance_infos": [
                {
                  "currency": "CNY",
                  "total_balance": "110.00",
                  "granted_balance": "10.00",
                  "topped_up_balance": "100.00"
                }
              ]
            }
            """));
        using var client = new HttpClient(handler);
        var service = new DeepSeekAccountService(client);

        var result = await service.GetBalanceAsync("  test-secret-key  ", CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(DeepSeekAccountService.BalanceEndpoint, handler.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-secret-key"), handler.Authorization);
        Assert.True(result.IsAvailable);
        var balance = Assert.Single(result.Balances);
        Assert.Equal("CNY", balance.Currency);
        Assert.Equal(110.00m, balance.TotalBalance);
        Assert.Equal(10.00m, balance.GrantedBalance);
        Assert.Equal(100.00m, balance.ToppedUpBalance);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API-E601")]
    [InlineData(HttpStatusCode.Forbidden, "API-E601")]
    [InlineData(HttpStatusCode.TooManyRequests, "API-E602")]
    [InlineData(HttpStatusCode.InternalServerError, "API-E604")]
    [InlineData(HttpStatusCode.BadRequest, "API-E606")]
    public async Task HttpFailuresMapToStableAccountErrors(HttpStatusCode statusCode, string expectedCode)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        using var client = new HttpClient(handler);
        var service = new DeepSeekAccountService(client);

        var exception = await Assert.ThrowsAsync<DeepSeekAccountException>(
            () => service.GetBalanceAsync("secret", CancellationToken.None));

        Assert.Equal(expectedCode, exception.Error.Code);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedDocumentedAmountsReturnInvalidResponseError()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            """
            {
              "is_available": true,
              "balance_infos": [{
                "currency": "CNY",
                "total_balance": "not-a-number",
                "granted_balance": "0",
                "topped_up_balance": "0"
              }]
            }
            """));
        using var client = new HttpClient(handler);
        var service = new DeepSeekAccountService(client);

        var exception = await Assert.ThrowsAsync<DeepSeekAccountException>(
            () => service.GetBalanceAsync("secret", CancellationToken.None));

        Assert.Equal("API-E605", exception.Error.Code);
    }

    [Fact]
    public async Task AccountViewModelUsesSystemKeyForEmptyQueriesAndCanClearIt()
    {
        var service = new StubAccountService();
        var viewModel = new AccountViewModel(
            service,
            new StubApiKeyProvider("sk-system-5678"),
            new StubLinkLauncher());

        await viewModel.RefreshAsync("sk-session-1234", CancellationToken.None);
        await viewModel.RefreshAsync(null, CancellationToken.None);

        Assert.Equal(2, service.CallCount);
        Assert.Equal("sk-system-5678", service.LastApiKey);
        Assert.True(viewModel.HasApiKey);
        Assert.Equal("****5678", viewModel.MaskedApiKey);
        Assert.True(viewModel.HasBalances);

        viewModel.ClearApiKey();

        Assert.False(viewModel.HasApiKey);
        Assert.False(viewModel.HasBalances);
        Assert.Equal("未设置", viewModel.MaskedApiKey);
    }

    [Fact]
    public async Task AccountViewModelRejectsMissingKeyWithoutCallingApi()
    {
        var service = new StubAccountService();
        var viewModel = new AccountViewModel(service, new StubApiKeyProvider(null), new StubLinkLauncher());

        await viewModel.RefreshAsync(null, CancellationToken.None);

        Assert.Equal(0, service.CallCount);
        Assert.Contains("API-E600", viewModel.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountViewModelUsesManualKeyForCurrentQuery()
    {
        var service = new StubAccountService();
        var viewModel = new AccountViewModel(service, new StubApiKeyProvider("sk-system"), new StubLinkLauncher());

        await viewModel.RefreshAsync(" sk-manual ", CancellationToken.None);

        Assert.Equal("sk-manual", service.LastApiKey);
    }

    [Fact]
    public void AccountTopUpCommandUsesFixedOfficialResource()
    {
        var linkLauncher = new StubLinkLauncher();
        var viewModel = new AccountViewModel(
            new StubAccountService(),
            new StubApiKeyProvider(null),
            linkLauncher);

        viewModel.OpenTopUpCommand.Execute(null);

        Assert.Equal(OfficialResource.DeepSeekTopUp, linkLauncher.LastResource);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StubAccountService : IDeepSeekAccountService
    {
        public int CallCount { get; private set; }
        public string? LastApiKey { get; private set; }

        public Task<DeepSeekAccountSnapshot> GetBalanceAsync(
            string apiKey,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastApiKey = apiKey;
            return Task.FromResult(new DeepSeekAccountSnapshot(
                true,
                [new DeepSeekBalanceInfo("CNY", 20m, 5m, 15m)]));
        }
    }

    private sealed class StubApiKeyProvider(string? apiKey) : IDeepSeekApiKeyProvider
    {
        public Task<string?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(apiKey);
    }

    private sealed class StubLinkLauncher : IExternalLinkLauncher
    {
        public OfficialResource? LastResource { get; private set; }
        public void Open(OfficialResource resource) => LastResource = resource;
        public void Open(Uri uri) { }
    }
}
