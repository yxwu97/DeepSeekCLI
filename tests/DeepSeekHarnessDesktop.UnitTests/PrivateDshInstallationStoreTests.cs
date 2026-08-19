using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Utilities;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class PrivateDshInstallationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DSH-PrivateStore",
        Guid.NewGuid().ToString("N"));
    private readonly string _resources;
    private readonly string _nodePath;

    public PrivateDshInstallationStoreTests()
    {
        _resources = Path.Combine(_root, "resources");
        _nodePath = Path.Combine(_root, "node.exe");
        Directory.CreateDirectory(_resources);
        File.WriteAllText(_nodePath, string.Empty);
        File.WriteAllText(
            Path.Combine(_resources, "package.json"),
            JsonSerializer.Serialize(new
            {
                dependencies = new Dictionary<string, string>
                {
                    [DshPackageMetadata.PackageName] = DshPackageMetadata.ValidatedVersion,
                },
            }));
        File.WriteAllText(
            Path.Combine(_resources, "package-lock.json"),
            JsonSerializer.Serialize(new
            {
                name = "test",
                lockfileVersion = 3,
                packages = new Dictionary<string, object>
                {
                    [string.Empty] = new
                    {
                        dependencies = new Dictionary<string, string>
                        {
                            [DshPackageMetadata.PackageName] = DshPackageMetadata.ValidatedVersion,
                        },
                    },
                },
            }));
    }

    [Fact]
    public async Task VersionBecomesDiscoverableOnlyAfterAtomicActivation()
    {
        var store = CreateStore();
        var transaction = await store.CreateTransactionAsync(CancellationToken.None);
        CreateInstalledDsh(transaction.StagingPath);

        var candidate = await store.CommitVersionAsync(
            transaction,
            _nodePath,
            CancellationToken.None);

        Assert.Null(await store.FindActiveAsync(_nodePath, CancellationToken.None));
        await store.ActivateAsync(candidate, CancellationToken.None);
        var active = await store.FindActiveAsync(_nodePath, CancellationToken.None);

        Assert.NotNull(active);
        Assert.Equal(DshInstallationSource.Private, active.Source);
        Assert.Equal(candidate.EntryPointPath, active.EntryPointPath, ignoreCase: true);
        Assert.Equal(candidate.InstallId, active.InstallId);
        await store.CleanupAsync(transaction);
    }

    [Fact]
    public async Task CommitRejectsLockfileChangedDuringInstallation()
    {
        var store = CreateStore();
        var transaction = await store.CreateTransactionAsync(CancellationToken.None);
        CreateInstalledDsh(transaction.StagingPath);
        File.AppendAllText(Path.Combine(transaction.StagingPath, "package-lock.json"), " ");

        var exception = await Assert.ThrowsAsync<HarnessException>(() => store.CommitVersionAsync(
            transaction,
            _nodePath,
            CancellationToken.None));

        Assert.Equal("DSH-E214", exception.Error.Code);
        Assert.Null(await store.FindActiveAsync(_nodePath, CancellationToken.None));
        await store.CleanupAsync(transaction);
    }

    [Fact]
    public async Task ActiveBackupRecoversFromCorruptPrimaryPointer()
    {
        var store = CreateStore();
        var transaction = await store.CreateTransactionAsync(CancellationToken.None);
        CreateInstalledDsh(transaction.StagingPath);
        var candidate = await store.CommitVersionAsync(transaction, _nodePath, CancellationToken.None);
        await store.ActivateAsync(candidate, CancellationToken.None);
        await store.ActivateAsync(candidate, CancellationToken.None);
        File.WriteAllText(Path.Combine(_root, "store", "active.json"), "{}");

        var active = await store.FindActiveAsync(_nodePath, CancellationToken.None);

        Assert.NotNull(active);
        Assert.Equal(candidate.InstallId, active.InstallId);
    }

    [Fact]
    public async Task ResourceLockMustPinValidatedRootVersion()
    {
        File.WriteAllText(
            Path.Combine(_resources, "package-lock.json"),
            "{\"lockfileVersion\":3,\"packages\":{\"\":{\"dependencies\":{}}}}");
        var store = CreateStore();

        var exception = await Assert.ThrowsAsync<HarnessException>(
            () => store.CreateTransactionAsync(CancellationToken.None));

        Assert.Equal("DSH-E214", exception.Error.Code);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_root, "store", "staging")));
    }

    [Fact]
    public async Task CleanupRejectsTransactionOutsideControlledStagingRoot()
    {
        var store = CreateStore();
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var transaction = new PrivateDshInstallTransaction(outside, "invalid", new string('0', 64));

        await Assert.ThrowsAsync<ArgumentException>(() => store.CleanupAsync(transaction));

        Assert.True(Directory.Exists(outside));
    }

    private PrivateDshInstallationStore CreateStore() => new(
        () => Path.Combine(_root, "store"),
        () => _resources);

    private static void CreateInstalledDsh(string root)
    {
        var packageRoot = Path.Combine(root, "node_modules", "@deepseek-ai", "dsh");
        var entryPoint = Path.Combine(packageRoot, "lib", "bin.js");
        Directory.CreateDirectory(Path.GetDirectoryName(entryPoint)!);
        File.WriteAllText(entryPoint, string.Empty);
        File.WriteAllText(
            Path.Combine(packageRoot, "package.json"),
            JsonSerializer.Serialize(new
            {
                name = DshPackageMetadata.PackageName,
                version = DshPackageMetadata.ValidatedVersion,
                bin = new Dictionary<string, string> { ["dsh"] = "lib/bin.js" },
            }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
