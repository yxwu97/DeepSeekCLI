using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DependencyDiagnosticsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DSH-Diagnostics", Guid.NewGuid().ToString("N"));

    public DependencyDiagnosticsServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task GlobalDshIsPreferredAndReportedWithSystemDependencies()
    {
        CreateFile("dsh.cmd");
        CreateFile("node.exe");
        CreateFile("npx.cmd");
        var service = CreateService();

        var result = await service.DiagnoseAsync(CancellationToken.None);

        Assert.Equal(DependencyStatus.Available, result.WebView2.Status);
        Assert.Equal(DependencyStatus.Available, result.GlobalDsh.Status);
        Assert.Equal(DependencyStatus.Available, result.Node.Status);
        Assert.Equal(DependencyStatus.Available, result.Npx.Status);
        Assert.True(result.CanLaunchDsh);
        Assert.DoesNotContain(result.Errors, error => error.Code == "DSH-E101");
    }

    [Fact]
    public async Task NodeAndNpxCanPrepareDshWhenGlobalDshIsMissing()
    {
        CreateFile("node.exe");
        CreateFile("npx.cmd");

        var result = await CreateService().DiagnoseAsync(CancellationToken.None);

        Assert.Equal(DependencyStatus.Missing, result.GlobalDsh.Status);
        Assert.True(result.CanLaunchDsh);
        Assert.DoesNotContain(result.Errors, error => error.Code == "DSH-E101");
    }

    [Fact]
    public async Task ValidatedCachedDshIsInstalledAndDoesNotRequireNpx()
    {
        CreateFile("node.exe");
        var cacheRoot = Path.Combine(_root, "cache", "_npx");
        var entryPoint = CreateCachedDsh(cacheRoot, "valid", "0.1.0-rc.6", "lib/bin.js");

        var result = await CreateService(cacheRoot).DiagnoseAsync(CancellationToken.None);

        Assert.Equal(DependencyStatus.Available, result.GlobalDsh.Status);
        Assert.Equal(entryPoint, result.GlobalDsh.Path, ignoreCase: true);
        Assert.Equal("0.1.0-rc.6", result.GlobalDsh.Version);
        Assert.Equal(DependencyStatus.Missing, result.Npx.Status);
        Assert.True(result.CanLaunchDsh);
        Assert.DoesNotContain(result.Errors, error => error.Code == "DSH-E101");
    }

    [Fact]
    public async Task CacheLocatorRejectsWrongVersionAndBinMapping()
    {
        var node = Path.Combine(_root, "node.exe");
        CreateFile("node.exe");
        var cacheRoot = Path.Combine(_root, "cache", "_npx");
        CreateCachedDsh(cacheRoot, "wrong-version", "0.1.0-rc.7", "lib/bin.js");
        CreateCachedDsh(cacheRoot, "wrong-bin", "0.1.0-rc.6", "other.js");
        var locator = new NpxDshCacheLocator(() => cacheRoot);

        var result = await locator.FindAsync(node, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task MissingNpxBlocksPreparationAndReturnsStableError()
    {
        CreateFile("node.exe");

        var result = await CreateService().DiagnoseAsync(CancellationToken.None);

        Assert.False(result.CanLaunchDsh);
        Assert.Contains(result.Errors, error => error.Code == "DSH-E101");
    }

    private DependencyDiagnosticsService CreateService(string? cacheRoot = null) => new(
        _ => _root,
        () => "140.0.0.0",
        (path, _) => Task.FromResult<string?>(Path.GetFileName(path) == "node.exe" ? "v24.15.0" : "0.1.0-rc.6"),
        new NpxDshCacheLocator(() => cacheRoot ?? Path.Combine(_root, "empty-cache")));

    private void CreateFile(string name) => File.WriteAllText(Path.Combine(_root, name), string.Empty);

    private static string CreateCachedDsh(
        string cacheRoot,
        string cacheId,
        string version,
        string binEntry)
    {
        var packageRoot = Path.Combine(cacheRoot, cacheId, "node_modules", "@deepseek-ai", "dsh");
        var entryPoint = Path.Combine(packageRoot, "lib", "bin.js");
        Directory.CreateDirectory(Path.GetDirectoryName(entryPoint)!);
        File.WriteAllText(entryPoint, string.Empty);
        File.WriteAllText(
            Path.Combine(packageRoot, "package.json"),
            JsonSerializer.Serialize(new
            {
                name = "@deepseek-ai/dsh",
                version,
                bin = new Dictionary<string, string> { ["dsh"] = binEntry },
            }));
        return entryPoint;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
