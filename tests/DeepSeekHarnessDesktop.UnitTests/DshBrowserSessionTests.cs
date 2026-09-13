using DeepSeekHarnessDesktop.Services;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DshBrowserSessionTests
{
    private static readonly Uri Origin = new("http://127.0.0.1:3080/");
    private static readonly string Token = new('a', 43);

    [Fact]
    public void ExplicitExternalLinkIsBoundToConfiguredOriginAndCleared()
    {
        var session = new DshBrowserSession();
        Assert.True(session.TryBeginExternal(Origin, $"{Origin}?token={Token}"));
        Assert.NotNull(session.GetAuthenticationUri(Origin));
        Assert.Null(session.GetAuthenticationUri(new Uri("http://localhost:3080/")));
        session.ClearExternal();
        Assert.Null(session.GetAuthenticationUri(Origin));
        session.Begin(Origin);
        session.CaptureOutput($"dsh web: {Origin}?token={Token}");
        session.ClearExternal();
        Assert.NotNull(session.GetAuthenticationUri(Origin));
    }

    [Theory]
    [InlineData("http://127.0.0.1:3081/?token={0}")]
    [InlineData("https://127.0.0.1:3080/?token={0}")]
    [InlineData("http://127.0.0.1.example.com:3080/?token={0}")]
    [InlineData("http://user@127.0.0.1:3080/?token={0}")]
    [InlineData("http://127.0.0.1:3080/other?token={0}")]
    [InlineData("http://127.0.0.1:3080/?token={0}&x=1")]
    [InlineData("http://127.0.0.1:3080/?token={0}#fragment")]
    [InlineData("http://127.0.0.1:3080/?%74oken={0}")]
    [InlineData("http://127.0.0.1:3080/?token=short")]
    public void RejectsUntrustedExternalLinkWithoutRetainingIt(string template)
    {
        var session = new DshBrowserSession();
        Assert.False(session.TryBeginExternal(Origin, string.Format(template, Token)));
        Assert.Null(session.GetAuthenticationUri(Origin));
    }

    [Fact]
    public void CapturesOnlyForActiveOriginAndClearsAcrossLaunches()
    {
        var session = new DshBrowserSession();
        var line = $"dsh web: {Origin}?token={Token}";
        session.CaptureOutput(line);
        Assert.Null(session.GetAuthenticationUri(Origin));

        session.Begin(Origin);
        session.CaptureOutput(line);
        Assert.Equal(new Uri(Origin, $"?token={Token}"), session.GetAuthenticationUri(Origin));
        Assert.Null(session.GetAuthenticationUri(new Uri("http://127.0.0.1:3081/")));

        session.Begin(Origin);
        Assert.Null(session.GetAuthenticationUri(Origin));
        session.CaptureOutput(line);
        session.Clear();
        session.CaptureOutput(line);
        Assert.Null(session.GetAuthenticationUri(Origin));
    }

    [Theory]
    [InlineData("https://127.0.0.1:3080/?token={0}")]
    [InlineData("http://127.0.0.1:3081/?token={0}")]
    [InlineData("http://localhost:3080/?token={0}")]
    [InlineData("http://127.0.0.1.example.com:3080/?token={0}")]
    [InlineData("http://user@127.0.0.1:3080/?token={0}")]
    [InlineData("http://127.0.0.1:3080/index.html?token={0}")]
    [InlineData("http://127.0.0.1:3080/?token={0}#fragment")]
    [InlineData("http://127.0.0.1:3080/?token={0}&token={0}")]
    [InlineData("http://127.0.0.1:3080/?token={0}&extra=1")]
    [InlineData("http://127.0.0.1:3080/?token=short")]
    [InlineData("http://127.0.0.1:3080/?%74oken={0}")]
    public void RejectsMalformedOrForeignAuthenticationUrl(string template)
    {
        var session = new DshBrowserSession();
        session.Begin(Origin);
        session.CaptureOutput("dsh web: " + string.Format(template, Token));
        Assert.Null(session.GetAuthenticationUri(Origin));
    }

    [Theory]
    [InlineData("http://localhost:3080/")]
    [InlineData("http://[::1]:3080/")]
    public void LoopbackAliasUsesOnlyTheAnnouncedIpv4AuthenticationOrigin(string value)
    {
        var configured = new Uri(value);
        var session = new DshBrowserSession();
        session.Begin(configured);
        session.CaptureOutput($"dsh web: {Origin}?token={Token}");
        var actual = session.GetAuthenticationUri(configured);

        Assert.Equal(new Uri(Origin, $"?token={Token}"), actual);
        Assert.Equal(actual, session.GetAuthenticationUri(Origin));
    }

    [Fact]
    public void IgnoresEmbeddedAnnouncementAndNeverSelectsLanAddress()
    {
        var session = new DshBrowserSession();
        session.Begin(Origin);
        session.CaptureOutput($"untrusted text dsh web: {Origin}?token={Token}");
        Assert.Null(session.GetAuthenticationUri(Origin));

        session.CaptureOutput($"dsh web: {Origin}?token={Token} (LAN: http://192.168.1.1:3080/?token={Token})");
        Assert.Equal(new Uri(Origin, $"?token={Token}"), session.GetAuthenticationUri(Origin));
    }
}
