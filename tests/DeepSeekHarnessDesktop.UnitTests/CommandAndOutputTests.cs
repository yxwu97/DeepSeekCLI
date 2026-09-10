using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Diagnostics;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class CommandAndOutputTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "DSH-UnitTests", Guid.NewGuid().ToString("N"));

    public CommandAndOutputTests() => Directory.CreateDirectory(_temporaryDirectory);

    [Fact]
    public void NpmEnvironmentRemovalClearsAllConfigVariantsAndTokens()
    {
        const string configName = "NpM_ConFiG_Registry";
        const string tokenName = "NODE_AUTH_TOKEN";
        var oldConfig = Environment.GetEnvironmentVariable(configName);
        var oldToken = Environment.GetEnvironmentVariable(tokenName);
        try
        {
            var environment = ProcessEnvironmentPolicy.Apply(
                new Dictionary<string, string>
                {
                    [configName] = "https://malicious.invalid",
                    [tokenName] = "secret",
                },
                new Dictionary<string, string> { ["SAFE_VALUE"] = "1" },
                [tokenName],
                ["NPM_CONFIG_"]);

            Assert.False(environment.ContainsKey(configName));
            Assert.False(environment.ContainsKey(tokenName));
            Assert.Equal("1", environment["SAFE_VALUE"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(configName, oldConfig);
            Environment.SetEnvironmentVariable(tokenName, oldToken);
        }
    }

    [Fact]
    public void LockedNpmArgumentsFixRegistryConfigCacheAndScriptPolicy()
    {
        var root = Path.Combine(Path.GetTempPath(), "npm-policy-test");
        var arguments = NpmCommandLineBuilder.CreateLockedInstallArguments(
            Path.Combine(root, "cache"),
            Path.Combine(root, "user.npmrc"),
            Path.Combine(root, "global.npmrc"));

        Assert.Contains("--ignore-scripts", arguments);
        Assert.Contains("--registry=https://registry.npmjs.org", arguments);
        Assert.Contains("--offline=false", arguments);
        Assert.Contains("--replace-registry-host=always", arguments);
        Assert.Contains(arguments, value => value.StartsWith("--userconfig=", StringComparison.Ordinal));
        Assert.Contains(arguments, value => value.StartsWith("--globalconfig=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolverPrefersGlobalDshInAutoMode()
    {
        CreateFile("dsh.cmd");
        CreateFile("node.exe");
        CreateFile("npm.cmd");
        CreateFile("npx.cmd");
        var cacheRoot = Path.Combine(_temporaryDirectory, "cache", "_npx");
        CreateCachedDsh(cacheRoot, "valid", DshPackageMetadata.ValidatedVersion, "lib/bin.js");
        var resolver = CreateResolver(cacheRoot);

        var options = await resolver.ResolveAsync(CreateSettings(), CancellationToken.None);

        Assert.EndsWith("dsh.cmd", options.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["web", "--no-open"], options.Arguments);
    }

    [Fact]
    public async Task ResolverRequiresPreparationInsteadOfRunningDynamicNpx()
    {
        CreateFile("node.exe");
        CreateFile("npm.cmd");
        CreateFile("npx.cmd");
        var discovery = new DshCandidateDiscoveryService(
            new EnvironmentPathProvider((_, _) => _temporaryDirectory),
            new FixedPrivateStore(null),
            new NpxDshCacheLocator(() => Path.Combine(_temporaryDirectory, "empty-cache")));
        var resolver = new DshCommandResolver(discovery: discovery);

        var exception = await Assert.ThrowsAsync<HarnessException>(() => resolver.ResolveAsync(
            CreateSettings(),
            CancellationToken.None));

        Assert.Equal("DSH-E101", exception.Error.Code);
        Assert.Contains("preparation", exception.Error.TechnicalMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolverReusesValidatedCachedDshBeforeNpx()
    {
        var node = CreateFile("node.exe");
        CreateFile("npx.cmd");
        var cacheRoot = Path.Combine(_temporaryDirectory, "cache", "_npx");
        var entryPoint = CreateCachedDsh(cacheRoot, "valid", DshPackageMetadata.ValidatedVersion, "lib/bin.js");
        var discovery = new DshCandidateDiscoveryService(
            new EnvironmentPathProvider((_, _) => _temporaryDirectory),
            new FixedPrivateStore(null),
            new NpxDshCacheLocator(() => cacheRoot));
        var resolver = new DshCommandResolver(discovery: discovery);

        var options = await resolver.ResolveAsync(CreateSettings(), CancellationToken.None);

        Assert.Equal(node, options.ExecutablePath, ignoreCase: true);
        Assert.Equal([entryPoint, "web", "--no-open"], options.Arguments);
    }

    [Fact]
    public async Task DiscoveryPrefersGlobalThenPrivateBeforeNpxCache()
    {
        var node = CreateFile("node.exe");
        var global = CreateFile("dsh.cmd");
        var privateEntry = CreateFile("private-bin.js");
        var privateStore = new FixedPrivateStore(new DshInstallationCandidate(
            DshInstallationSource.Private,
            node,
            privateEntry,
            DshPackageMetadata.ValidatedVersion,
            "private-test"));
        var cacheRoot = Path.Combine(_temporaryDirectory, "priority-cache", "_npx");
        CreateCachedDsh(cacheRoot, "valid", DshPackageMetadata.ValidatedVersion, "lib/bin.js");
        var discovery = new DshCandidateDiscoveryService(
            new EnvironmentPathProvider((_, _) => _temporaryDirectory),
            privateStore,
            new NpxDshCacheLocator(() => cacheRoot),
            new FixedVersionProbe(DshPackageMetadata.ValidatedVersion));

        var first = await discovery.DiscoverAsync(CancellationToken.None);
        var privateFindsAfterGlobal = privateStore.FindCount;
        File.Delete(global);
        var second = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(DshInstallationSource.GlobalPath, first.Candidate?.Source);
        Assert.Equal(0, privateFindsAfterGlobal);
        Assert.Equal(DshInstallationSource.Private, second.Candidate?.Source);
        Assert.Equal(1, privateStore.FindCount);
    }

    [Theory]
    [InlineData("0.1.0-rc.6")]
    [InlineData("0.1.0-rc.8")]
    [InlineData("0.1.0")]
    [InlineData("0.1.1-rc.2")]
    [InlineData("0.1.5-rc.2")]
    public async Task DiscoveryRejectsUnvalidatedGlobalAndFallsBackToValidatedPrivate(string globalVersion)
    {
        var node = CreateFile("node.exe");
        CreateFile("dsh.cmd");
        var privateEntry = CreateFile("private-bin.js");
        var privateStore = new FixedPrivateStore(new DshInstallationCandidate(
            DshInstallationSource.Private,
            node,
            privateEntry,
            DshPackageMetadata.ValidatedVersion,
            "private-test"));
        var discovery = new DshCandidateDiscoveryService(
            new EnvironmentPathProvider((_, _) => _temporaryDirectory),
            privateStore,
            new NpxDshCacheLocator(() => Path.Combine(_temporaryDirectory, "empty-cache")),
            new FixedVersionProbe(globalVersion));

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(DshInstallationSource.Private, result.Candidate?.Source);
        Assert.Equal(globalVersion, result.RejectedGlobalDsh?.ActualVersion);
        Assert.Equal(1, privateStore.FindCount);
    }

    [Fact]
    public async Task ResolverAddsOnlyValidatedNonDefaultPort()
    {
        CreateFile("dsh.cmd");
        var resolver = CreateResolver(Path.Combine(_temporaryDirectory, "empty-cache"));
        var settings = CreateSettings() with { ServiceUri = new Uri("http://127.0.0.1:65535/") };

        var options = await resolver.ResolveAsync(settings, CancellationToken.None);

        Assert.Equal(["web", "--no-open", "--port", "65535"], options.Arguments);
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
        File.WriteAllText(script, "@echo CMD_BUILDER_OK\r\n");
        var startInfo = CmdCommandLineBuilder.Build(
            script,
            ["web", "--no-open"],
            _temporaryDirectory,
            new Dictionary<string, string>());

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

    [Fact]
    public void CmdBuilderRejectsTemplateThatOpensDefaultBrowser()
    {
        var script = CreateFile("dsh.cmd");

        Assert.Throws<ArgumentException>(() => CmdCommandLineBuilder.Build(
            script, ["web"], _temporaryDirectory, new Dictionary<string, string>()));
    }

    [Fact]
    public void CmdBuilderRejectsFormerAutomaticNpxFallback()
    {
        var script = CreateFile("npx.cmd");

        Assert.Throws<ArgumentException>(() => CmdCommandLineBuilder.Build(
            script,
            ["-y", DshPackageMetadata.ValidatedPackageSpec, "web"],
            _temporaryDirectory,
            new Dictionary<string, string>()));
    }

    [Fact]
    public void NpmBuilderAllowsOnlyLockedCiCommand()
    {
        var script = CreateFile("npm.cmd");

        var startInfo = NpmCommandLineBuilder.BuildLockedInstall(
            script,
            _temporaryDirectory,
            new Dictionary<string, string>());

        Assert.EndsWith("cmd.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ci --omit=dev", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => NpmCommandLineBuilder.Build(
            script,
            ["install", "evil-package"],
            _temporaryDirectory,
            new Dictionary<string, string>()));
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
            script,
            ["web", "--no-open", "--port", port],
            _temporaryDirectory,
            new Dictionary<string, string>()));
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

    private DshCommandResolver CreateResolver(string cacheRoot)
    {
        var pathProvider = new EnvironmentPathProvider((_, _) => _temporaryDirectory);
        var discovery = new DshCandidateDiscoveryService(
            pathProvider,
            new FixedPrivateStore(null),
            cacheLocator: new NpxDshCacheLocator(() => cacheRoot),
            versionProbe: new FixedVersionProbe(DshPackageMetadata.ValidatedVersion));
        return new DshCommandResolver(discovery: discovery);
    }

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

    private sealed class FixedPrivateStore(DshInstallationCandidate? candidate)
        : IPrivateDshInstallationStore
    {
        public int FindCount { get; private set; }

        public Task<DshInstallationCandidate?> FindActiveAsync(
            string? nodePath,
            CancellationToken cancellationToken)
        {
            FindCount++;
            return Task.FromResult(candidate);
        }

        public Task<PrivateDshInstallTransaction> CreateTransactionAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PrivateDshInstallTransaction> CreateTransactionAsync(
            DshRuntimeDescriptor descriptor,
            string packagePath,
            string lockPath,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DshInstallationCandidate> CommitVersionAsync(
            PrivateDshInstallTransaction transaction,
            string nodePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ActivateAsync(
            DshInstallationCandidate value,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CleanupAsync(PrivateDshInstallTransaction transaction) =>
            throw new NotSupportedException();
    }

    private sealed class FixedVersionProbe(string version) : IDshVersionProbe
    {
        public Task<DshVersionProbeResult> ProbeAsync(
            string executablePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DshVersionProbeResult(true, version));
    }
}
