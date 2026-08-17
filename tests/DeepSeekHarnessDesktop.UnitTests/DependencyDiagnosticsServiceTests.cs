using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DependencyDiagnosticsServiceTests
{
    [Fact]
    public async Task ReportsVersionsWhenDependenciesExist()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "node.exe"), string.Empty);
        File.WriteAllText(Path.Combine(directory.Path, "npx.cmd"), string.Empty);
        var service = new DependencyDiagnosticsService(
            name => name == "PATH" ? directory.Path : null,
            () => "140.0.3485.54",
            (_, _) => Task.FromResult<string?>("v24.1.0"));

        var result = await service.DiagnoseAsync(CancellationToken.None);

        Assert.Equal("140.0.3485.54", result.WebView2RuntimeVersion);
        Assert.Equal("v24.1.0", result.NodeVersion);
        Assert.EndsWith("npx.cmd", result.NpxPath, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task MissingDependenciesReturnStableErrorCodes()
    {
        var service = new DependencyDiagnosticsService(
            _ => null,
            () => throw new InvalidOperationException("runtime missing"),
            (_, _) => Task.FromResult<string?>(null));

        var result = await service.DiagnoseAsync(CancellationToken.None);

        Assert.Contains(result.Errors, error => error.Code == "WEB-E301");
        Assert.Contains(result.Errors, error => error.Code == "DSH-E101");
    }

    [Fact]
    public async Task GlobalDshIsSufficientWithoutNodeOrNpx()
    {
        using var directory = new TemporaryDirectory();
        var dsh = Path.Combine(directory.Path, "dsh.cmd");
        File.WriteAllText(dsh, string.Empty);
        var service = new DependencyDiagnosticsService(
            name => name == "PATH" ? directory.Path : null,
            () => "140.0.3485.54",
            (_, _) => Task.FromResult<string?>("0.1.0-rc.6"));

        var result = await service.DiagnoseAsync(CancellationToken.None);

        Assert.True(result.CanLaunchDsh);
        Assert.Equal(DependencyStatus.Available, result.GlobalDsh.Status);
        Assert.Equal(DependencyStatus.Missing, result.Node.Status);
        Assert.DoesNotContain(result.Errors, error => error.Code == "DSH-E101");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-dependency-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
