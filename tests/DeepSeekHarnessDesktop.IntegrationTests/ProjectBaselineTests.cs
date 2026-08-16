using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.IntegrationTests;

public sealed class ProjectBaselineTests
{
    [Fact]
    public void DefaultServiceUriIsLoopback()
    {
        var settings = new AppSettings();

        Assert.True(settings.ServiceUri.IsLoopback);
        Assert.Equal("http", settings.ServiceUri.Scheme);
    }
}
