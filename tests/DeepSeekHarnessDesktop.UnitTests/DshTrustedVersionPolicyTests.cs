using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DshTrustedVersionPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DSH-TrustedVersion",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsToBootstrapDescriptor()
    {
        var policy = new DshTrustedVersionPolicy(() => _root);

        Assert.Equal(DshPackageMetadata.BootstrapVersion, policy.Current.Version);
        Assert.Equal(DshPackageMetadata.RuntimeProtocol, policy.Current.RuntimeProtocol);
        Assert.Equal("bootstrap", policy.Current.Source);
    }

    [Fact]
    public async Task SelectedDescriptorPersistsAcrossInstances()
    {
        var descriptor = new DshRuntimeDescriptor(
            "0.1.1-rc.3",
            DshPackageMetadata.RuntimeProtocol,
            "0.11.0",
            ">=20 <25",
            "catalog:12",
            new string('a', 64));
        var policy = new DshTrustedVersionPolicy(() => _root);

        await policy.SelectAsync(descriptor, CancellationToken.None);
        var reloaded = new DshTrustedVersionPolicy(() => _root);

        Assert.Equal(descriptor, reloaded.Current);
    }

    [Fact]
    public async Task IncompatibleProtocolIsRejectedWithoutChangingSelection()
    {
        var policy = new DshTrustedVersionPolicy(() => _root);
        var descriptor = policy.Bootstrap with { RuntimeProtocol = 2 };

        var exception = await Assert.ThrowsAsync<HarnessException>(
            () => policy.SelectAsync(descriptor, CancellationToken.None));

        Assert.Equal("DSH-E227", exception.Error.Code);
        Assert.Equal(policy.Bootstrap, policy.Current);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
