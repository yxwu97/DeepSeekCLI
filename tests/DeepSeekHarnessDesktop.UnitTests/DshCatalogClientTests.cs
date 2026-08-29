using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DshCatalogClientTests : IDisposable
{
    private const string KeyId = "test-2026";
    private static readonly Uri CatalogUri = new("https://catalog.example.test/catalog-v1.json");
    private static readonly Uri SignatureUri = new("https://catalog.example.test/catalog-v1.sig");
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DSH-CatalogClient",
        Guid.NewGuid().ToString("N"));
    private readonly RSA _rsa = new RSACng(3072);

    [Fact]
    public async Task HigherSequenceIsAcceptedAndReplayIsRejected()
    {
        var current = CatalogBytes(12, "0.1.1-rc.3");
        var replay = CatalogBytes(11, "0.1.1-rc.2");
        var handler = new QueueHandler(
            Response(current), Response(Sign(current)),
            Response(replay), Response(Sign(replay)));
        using var client = CreateClient(handler);

        var accepted = await client.LoadAsync(CancellationToken.None);
        var rejected = await client.LoadAsync(CancellationToken.None);

        Assert.True(accepted.Succeeded);
        Assert.Equal(12, accepted.Snapshot!.CatalogSequence);
        Assert.False(rejected.Succeeded);
        Assert.Equal("DSH-E226", rejected.Error!.Code);
        Assert.Contains("replay", rejected.Error.TechnicalMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SameSequenceWithDifferentBytesIsRejected()
    {
        var first = CatalogBytes(12, "0.1.1-rc.3");
        var collision = CatalogBytes(12, "0.1.1-rc.4");
        var handler = new QueueHandler(
            Response(first), Response(Sign(first)),
            Response(collision), Response(Sign(collision)));
        using var client = CreateClient(handler);

        Assert.True((await client.LoadAsync(CancellationToken.None)).Succeeded);
        var rejected = await client.LoadAsync(CancellationToken.None);

        Assert.Equal("DSH-E226", rejected.Error!.Code);
        Assert.Contains("collision", rejected.Error.TechnicalMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkFailureUsesPreviouslyVerifiedCache()
    {
        var bytes = CatalogBytes(12, "0.1.1-rc.3");
        using (var online = CreateClient(new QueueHandler(Response(bytes), Response(Sign(bytes)))))
        {
            Assert.True((await online.LoadAsync(CancellationToken.None)).Succeeded);
        }
        using var offline = CreateClient(new QueueHandler(new HttpRequestException("offline")));

        var result = await offline.LoadAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.FromCache);
        Assert.Equal(12, result.Snapshot!.CatalogSequence);
    }

    private DshCatalogClient CreateClient(HttpMessageHandler handler)
    {
        var verifier = new DshCatalogVerifier(
            new Dictionary<string, RSAParameters> { [KeyId] = _rsa.ExportParameters(false) },
            "assets.example.test");
        return new DshCatalogClient(
            new HttpClient(handler),
            verifier,
            CatalogUri,
            SignatureUri,
            () => _root);
    }

    private byte[] CatalogBytes(long sequence, string version) => Encoding.UTF8.GetBytes(
        "{"
        + "\"schemaVersion\":1,"
        + $"\"catalogSequence\":{sequence},"
        + "\"generatedAt\":\"2026-08-29T00:00:00Z\","
        + $"\"signingKeyId\":\"{KeyId}\","
        + "\"entries\":[{"
        + $"\"version\":\"{version}\","
        + "\"publishedAt\":\"2026-08-29T00:00:00Z\","
        + "\"runtimeProtocol\":1,"
        + "\"minimumDesktopVersion\":\"0.11.0\","
        + "\"nodeVersionRange\":\">=20 <25\","
        + "\"revoked\":false,"
        + $"\"package\":{{\"url\":\"https://assets.example.test/runtime/package.json\",\"bytes\":100,\"sha256\":\"{new string('a', 64)}\"}},"
        + $"\"lock\":{{\"url\":\"https://assets.example.test/runtime/package-lock.json\",\"bytes\":200,\"sha256\":\"{new string('b', 64)}\"}}"
        + "}]}" );

    private byte[] Sign(byte[] bytes) => _rsa.SignData(
        bytes,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);

    private static HttpResponseMessage Response(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    };

    public void Dispose()
    {
        _rsa.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<object> _responses;

        public QueueHandler(params object[] responses) => _responses = new Queue<object>(responses);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var next = _responses.Dequeue();
            if (next is Exception exception)
            {
                return Task.FromException<HttpResponseMessage>(exception);
            }
            var response = (HttpResponseMessage)next;
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
