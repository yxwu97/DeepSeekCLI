using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DshVersionProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DSH-VersionProbe",
        Guid.NewGuid().ToString("N"));

    public DshVersionProbeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ExactValidatedVersionIsAcceptedAndNormalized()
    {
        var script = CreateScript("success.cmd", $"echo {DshPackageMetadata.ValidatedVersion}");

        var result = await new DshVersionProbe().ProbeAsync(script, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(DshPackageMetadata.ValidatedVersion, result.Version);
    }

    [Fact]
    public async Task ControlledCmdProbeSupportsSpecialUnicodePath()
    {
        var directory = Path.Combine(_root, "space & (中文)");
        Directory.CreateDirectory(directory);
        var script = CreateScript(
            Path.Combine("space & (中文)", "dsh probe & ok.cmd"),
            $"echo {DshPackageMetadata.ValidatedVersion}");

        var result = await new DshVersionProbe().ProbeAsync(script, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(DshPackageMetadata.ValidatedVersion, result.Version);
    }

    [Theory]
    [InlineData("echo 0.1.0-rc.7&echo unexpected")]
    [InlineData("echo not-a-version")]
    [InlineData("echo failed 1^>^&2&exit /b 23")]
    public async Task InvalidOutputOrExitIsRejected(string body)
    {
        var result = await new DshVersionProbe().ProbeAsync(
            CreateScript("invalid.cmd", body),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task OversizedOutputIsRejected()
    {
        var body = "echo " + new string('x', DshVersionProbe.MaximumOutputCharacters + 1);

        var result = await new DshVersionProbe().ProbeAsync(
            CreateScript("oversized.cmd", body),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("limit", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TimeoutReturnsFailureAndReleasesProcess()
    {
        var script = CreateScript("timeout.cmd", "ping 127.0.0.1 -n 10 >nul");

        var result = await new DshVersionProbe().ProbeAsync(script, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.Detail, StringComparison.OrdinalIgnoreCase);
        AssertFileCanBeReplaced(script);
    }

    [Fact]
    public async Task CallerCancellationIsPropagatedAndReleasesProcess()
    {
        var script = CreateScript("cancel.cmd", "ping 127.0.0.1 -n 10 >nul");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DshVersionProbe().ProbeAsync(script, cancellation.Token));

        AssertFileCanBeReplaced(script);
    }

    private string CreateScript(string name, string body)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, $"@echo off\r\n{body}\r\n");
        return path;
    }

    private static void AssertFileCanBeReplaced(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(stream.CanWrite);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
