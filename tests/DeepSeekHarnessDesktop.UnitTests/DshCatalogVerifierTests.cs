using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using System.Security.Cryptography;
using System.Text;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DshCatalogVerifierTests : IDisposable
{
    private const string KeyId = "test-2026";
    private readonly RSA _rsa = new RSACng(3072);
    private readonly DshCatalogVerifier _verifier;

    public DshCatalogVerifierTests()
    {
        _verifier = new DshCatalogVerifier(
            new Dictionary<string, RSAParameters> { [KeyId] = _rsa.ExportParameters(false) },
            "github.example.test");
    }

    [Fact]
    public void ValidDetachedSignatureAndCatalogAreAccepted()
    {
        var bytes = CatalogBytes();

        var result = _verifier.VerifyAndParse(bytes, Sign(bytes));

        Assert.Equal(12, result.CatalogSequence);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("0.1.1-rc.3", entry.Version);
        Assert.Equal(1, entry.RuntimeProtocol);
        Assert.False(entry.Revoked);
    }

    [Fact]
    public void AnyCatalogByteTamperIsRejected()
    {
        var bytes = CatalogBytes();
        var signature = Sign(bytes);
        bytes[bytes.Length - 2] ^= 1;

        var exception = Assert.Throws<HarnessException>(
            () => _verifier.VerifyAndParse(bytes, signature));

        Assert.Equal("DSH-E226", exception.Error.Code);
    }

    [Fact]
    public void DuplicatePropertyIsRejectedBeforeParsing()
    {
        var bytes = CatalogBytes(extraRoot: ",\"schemaVersion\":1");

        var exception = Assert.Throws<HarnessException>(
            () => _verifier.VerifyAndParse(bytes, Sign(bytes)));

        Assert.Equal("DSH-E226", exception.Error.Code);
        Assert.Contains("Duplicate", exception.Error.TechnicalMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCriticalFieldIsRejected()
    {
        var bytes = CatalogBytes(extraRoot: ",\"command\":\"npm install latest\"");

        var exception = Assert.Throws<HarnessException>(
            () => _verifier.VerifyAndParse(bytes, Sign(bytes)));

        Assert.Equal("DSH-E226", exception.Error.Code);
    }

    [Fact]
    public void AssetOutsideFixedHttpsHostIsRejected()
    {
        var bytes = CatalogBytes(assetHost: "malicious.example");

        var exception = Assert.Throws<HarnessException>(
            () => _verifier.VerifyAndParse(bytes, Sign(bytes)));

        Assert.Equal("DSH-E226", exception.Error.Code);
    }

    private byte[] CatalogBytes(
        string extraRoot = "",
        string assetHost = "github.example.test") => Encoding.UTF8.GetBytes(
        "{"
        + "\"schemaVersion\":1,"
        + "\"catalogSequence\":12,"
        + "\"generatedAt\":\"2026-08-29T00:00:00Z\","
        + $"\"signingKeyId\":\"{KeyId}\","
        + "\"entries\":[{"
        + "\"version\":\"0.1.1-rc.3\","
        + "\"publishedAt\":\"2026-08-29T00:00:00Z\","
        + "\"runtimeProtocol\":1,"
        + "\"minimumDesktopVersion\":\"0.11.0\","
        + "\"nodeVersionRange\":\">=20 <25\","
        + "\"revoked\":false,"
        + $"\"package\":{{\"url\":\"https://{assetHost}/runtime/package.json\",\"bytes\":100,\"sha256\":\"{new string('a', 64)}\"}},"
        + $"\"lock\":{{\"url\":\"https://{assetHost}/runtime/package-lock.json\",\"bytes\":200,\"sha256\":\"{new string('b', 64)}\"}}"
        + "}]"
        + extraRoot
        + "}");

    private byte[] Sign(byte[] bytes) => _rsa.SignData(
        bytes,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);

    public void Dispose() => _rsa.Dispose();
}
