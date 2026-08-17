using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class ServiceUriValidatorTests
{
    [Theory]
    [InlineData("http://127.0.0.1:3080/", "http://127.0.0.1:3080/")]
    [InlineData("http://localhost:43123/path", "http://localhost:43123/")]
    [InlineData("https://[::1]:8443/ui", "https://[::1]:8443/")]
    public void NormalizesSupportedLoopbackOrigins(string value, string expected)
    {
        Assert.True(ServiceUriValidator.TryNormalize(value, out var normalized, out _));
        Assert.Equal(expected, normalized.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("ftp://127.0.0.1/")]
    [InlineData("http://user:password@127.0.0.1:3080/")]
    [InlineData("http://127.0.0.1:3080/?token=secret")]
    [InlineData("http://127.0.0.1:3080/#fragment")]
    [InlineData("http://127.0.0.1:0/")]
    [InlineData("http://127.0.0.1:65536/")]
    [InlineData("not-a-uri")]
    public void RejectsUnsafeOrInvalidOrigins(string value)
    {
        Assert.False(ServiceUriValidator.TryNormalize(value, out _, out _));
    }
}
