using DeepSeekHarnessDesktop.Services;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public async Task SecondInstanceIsNotPrimary()
    {
        var instanceId = $"test-{Guid.NewGuid():N}";
        await using var primary = new SingleInstanceService(instanceId);
        await using var secondary = new SingleInstanceService(instanceId);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        Assert.False(await primary.NotifyPrimaryAsync(CancellationToken.None));
    }
}
