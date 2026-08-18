using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Utilities;
using System.Diagnostics;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class CommandAndOutputTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "DSH-UnitTests", Guid.NewGuid().ToString("N"));

    public CommandAndOutputTests() => Directory.CreateDirectory(_temporaryDirectory);

    [Fact]
    public async Task ResolverPrefersGlobalDshInAutoMode()
    {
        CreateFile("dsh.cmd");
        CreateFile("node.exe");
        CreateFile("npx.cmd");
        var cacheRoot = Path.Combine(_temporaryDirectory, "cache", "_npx");
        CreateCachedDsh(cacheRoot, "valid", DshPackageMetadata.ValidatedVersion, "lib/bin.js");
        var resolver = new DshCommandResolver(
            _ => _temporaryDirectory,
            new NpxDshCacheLocator(() => cacheRoot));

        var options = await resolver.ResolveAsync(CreateSettings(), CancellationToken.None);

        Assert.EndsWith("dsh.cmd", options.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["web"], options.Arguments);
    }

    [Fact]
    public async Task ResolverUsesPinnedNpxPackageWhenGlobalDshIsMissing()
    {
        CreateFile("node.exe");
        CreateFile("npx.cmd");
        var resolver = new DshCommandResolver(
            _ => _temporaryDirectory,
            new NpxDshCacheLocator(() => Path.Combine(_temporaryDirectory, "empty-cache")));

        var options = await resolver.ResolveAsync(CreateSettings(), CancellationToken.None);

        Assert.EndsWith("npx.cmd", options.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["-y", DshPackageMetadata.ValidatedPackageSpec, "web"], options.Arguments);
    }

    [Fact]
    public async Task ResolverReusesValidatedCachedDshBeforeNpx()
    {
        var node = CreateFile("node.exe");
        CreateFile("npx.cmd");
        var cacheRoot = Path.Combine(_temporaryDirectory, "cache", "_npx");
        var entryPoint = CreateCachedDsh(cacheRoot, "valid", DshPackageMetadata.ValidatedVersion, "lib/bin.js");
        var resolver = new DshCommandResolver(
            _ => _temporaryDirectory,
            new NpxDshCacheLocator(() => cacheRoot));

        var options = await resolver.ResolveAsync(CreateSettings(), CancellationToken.None);

        Assert.Equal(node, options.ExecutablePath, ignoreCase: true);
        Assert.Equal([entryPoint, "web"], options.Arguments);
    }

    [Fact]
    public async Task ResolverAddsOnlyValidatedNonDefaultPort()
    {
        CreateFile("dsh.cmd");
        var resolver = new DshCommandResolver(_ => _temporaryDirectory);
        var settings = CreateSettings() with { ServiceUri = new Uri("http://127.0.0.1:65535/") };

        var options = await resolver.ResolveAsync(settings, CancellationToken.None);

        Assert.Equal(["web", "--port", "65535"], options.Arguments);
    }

    [Fact]
    public async Task ResolverRejectsCustomCmdFile()
    {
        var script = CreateFile("custom.cmd");
        var resolver = new DshCommandResolver();
        var settings = CreateSettings() with
        {
            Launch = new LaunchSettings { Mode = LaunchMode.Custom, ExecutablePath = script },
        };

        var exception = await Assert.ThrowsAsync<HarnessException>(() => resolver.ResolveAsync(
            settings,
            CancellationToken.None));

        Assert.Equal("DSH-E101", exception.Error.Code);
    }

    [Fact]
    public async Task CmdBuilderExecutesSpecialUnicodePath()
    {
        var directory = Path.Combine(_temporaryDirectory, "space & (中文)");
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "dsh.cmd");
        await File.WriteAllTextAsync(script, "@echo CMD_BUILDER_OK\r\n");
        var startInfo = CmdCommandLineBuilder.Build(script, ["web"], _temporaryDirectory, new Dictionary<string, string>());

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("CMD_BUILDER_OK", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CmdBuilderRejectsUserArguments()
    {
        var script = CreateFile("dsh.cmd");

        Assert.Throws<ArgumentException>(() => CmdCommandLineBuilder.Build(
            script, ["web", "&", "whoami"], _temporaryDirectory, new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("3080&whoami")]
    [InlineData("3080|")]
    [InlineData("3080^")]
    [InlineData("3080%PATH%")]
    [InlineData("3080!")]
    [InlineData("(3080)")]
    public void CmdBuilderRejectsInvalidPortTemplate(string port)
    {
        var script = CreateFile("dsh.cmd");

        Assert.Throws<ArgumentException>(() => CmdCommandLineBuilder.Build(
            script, ["web", "--port", port], _temporaryDirectory, new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData("\u001b[32mdsh web: http://127.0.0.1:3080/\u001b[0m", "http://127.0.0.1:3080/")]
    [InlineData("ready at http://localhost:12345/path.", "http://localhost:12345/")]
    [InlineData("ready (http://[::1]:8080/).", "http://[::1]:8080/")]
    [InlineData("https://example.com:3080/", null)]
    [InlineData("http://127.0.0.1:99999/", null)]
    public void UrlParserAcceptsOnlyValidLoopbackUris(string line, string? expected)
    {
        Assert.Equal(expected, UrlParser.TryParseLoopback(line)?.AbsoluteUri);
    }

    [Fact]
    public void OutputProcessorStripsAnsiAndBoundsLineLength()
    {
        var result = OutputLineProcessor.Normalize("\u001b[31m" + new string('x', 20_000) + "\u001b[0m");

        Assert.NotNull(result);
        Assert.Equal(OutputLineProcessor.MaximumLineLength, result.Length);
        Assert.EndsWith(OutputLineProcessor.TruncationMarker, result, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomNativeCommandArgumentsAreOmittedFromLogs()
    {
        var options = new DshLaunchOptions
        {
            ExecutablePath = Path.Combine(_temporaryDirectory, "custom.exe"),
            Arguments = ["--token", "secret-value"],
            WorkingDirectory = _temporaryDirectory,
            FallbackUri = new Uri("http://127.0.0.1:3080/"),
        };

        var text = LaunchCommandLogFormatter.Format(options);

        Assert.Equal("custom.exe <arguments omitted>", text);
        Assert.DoesNotContain("secret-value", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("npm ERR! code ENOTFOUND", "DSH-E211")]
    [InlineData("SELF_SIGNED_CERT_IN_CHAIN", "DSH-E212")]
    [InlineData("npm ERR! code E403", "DSH-E213")]
    [InlineData("npm ERR! code EACCES", "DSH-E214")]
    public void NpmFailuresMapToStableActionableErrors(string stderr, string expectedCode)
    {
        var error = NpmFailureClassifier.Classify([stderr]);

        Assert.Equal(expectedCode, error?.Code);
    }

    private AppSettings CreateSettings() => new()
    {
        WorkspacePath = _temporaryDirectory,
        AutoStart = false,
    };

    private string CreateFile(string name)
    {
        var path = Path.Combine(_temporaryDirectory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

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
                name = DshPackageMetadata.PackageName,
                version,
                bin = new Dictionary<string, string> { ["dsh"] = binEntry },
            }));
        return entryPoint;
    }

    public void Dispose() => Directory.Delete(_temporaryDirectory, recursive: true);
}
