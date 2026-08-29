using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DshUpdateServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DSH-UpdateServices",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LatestNewerThanCatalogOnlyProducesWaitingState()
    {
        var current = Descriptor(DshPackageMetadata.BootstrapVersion);
        var catalog = Snapshot(12, Entry("0.1.1-rc.3"));
        var service = new DshUpdateCheckService(
            new FakeReleaseService(new DshUpdateCheckResult("0.1.1-rc.4", DateTimeOffset.Now)),
            new FakeCatalogService(new DshCatalogLoadResult(catalog, false, DateTimeOffset.Now)),
            new FixedPolicy(current));

        var result = await service.CheckAsync("0.11.0", "v24.1.0", CancellationToken.None);

        Assert.Equal("0.1.1-rc.3", result.CatalogUpdate!.Entry.Version);
        Assert.True(result.WaitingForValidation);
        Assert.Equal("0.1.1-rc.4", result.NpmLatestVersion);
    }

    [Fact]
    public void HigherIncompatibleCatalogEntryReturnsStableProtocolError()
    {
        var entry = Entry("0.1.1-rc.3") with { RuntimeProtocol = 2 };

        var update = DshCatalogCompatibility.SelectUpdate(
            Snapshot(12, entry),
            Descriptor(DshPackageMetadata.BootstrapVersion),
            "0.11.0",
            "v24.0.0",
            out var error);

        Assert.Null(update);
        Assert.Equal("DSH-E227", error!.Code);
    }

    [Fact]
    public async Task AssetHashMismatchIsRejectedAndTransactionIsRemoved()
    {
        var package = System.Text.Encoding.UTF8.GetBytes("{\"name\":\"test\"}");
        var lockBytes = System.Text.Encoding.UTF8.GetBytes("{\"lockfileVersion\":3}");
        var entry = Entry("0.1.1-rc.3") with
        {
            Package = Asset("package.json", package, new string('0', 64)),
            Lock = Asset("package-lock.json", lockBytes, Sha256(lockBytes)),
        };
        using var downloader = new DshCatalogAssetDownloader(
            new HttpClient(new AssetHandler(package, lockBytes)),
            "assets.example.test",
            () => _root);

        var exception = await Assert.ThrowsAsync<HarnessException>(
            () => downloader.DownloadAsync(entry, CancellationToken.None));

        Assert.Equal("DSH-E226", exception.Error.Code);
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public async Task VerifiedAssetsAreWrittenWithFixedNamesAndCanBeCleaned()
    {
        var package = System.Text.Encoding.UTF8.GetBytes("{\"name\":\"test\"}");
        var lockBytes = System.Text.Encoding.UTF8.GetBytes("{\"lockfileVersion\":3}");
        var entry = Entry("0.1.1-rc.3") with
        {
            Package = Asset("package.json", package, Sha256(package)),
            Lock = Asset("package-lock.json", lockBytes, Sha256(lockBytes)),
        };
        using var downloader = new DshCatalogAssetDownloader(
            new HttpClient(new AssetHandler(package, lockBytes)),
            "assets.example.test",
            () => _root);

        var assets = await downloader.DownloadAsync(entry, CancellationToken.None);

        Assert.Equal(package, File.ReadAllBytes(assets.PackagePath));
        Assert.Equal(lockBytes, File.ReadAllBytes(assets.LockPath));
        await downloader.CleanupAsync(assets);
        Assert.False(Directory.Exists(assets.RootPath));
    }

    [Fact]
    public async Task ExternalRuntimeIsNeverStoppedOrUpdated()
    {
        var calls = new List<string>();
        var lifecycle = new UpdateLifecycle(HarnessRuntimeState.RunningExternal, calls);
        using var coordinator = new DshRuntimeUpdateCoordinator(
            lifecycle,
            new FakeUpdateInstaller(calls));

        var exception = await Assert.ThrowsAsync<HarnessException>(() => coordinator.ApplyAsync(
            Candidate("0.1.1-rc.3"),
            UpdateDiagnostics(),
            CancellationToken.None));

        Assert.Equal("DSH-E224", exception.Error.Code);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task OwnedRuntimeStopsInstallsAndStartsInOrder()
    {
        var calls = new List<string>();
        var lifecycle = new UpdateLifecycle(HarnessRuntimeState.RunningOwned, calls);
        using var coordinator = new DshRuntimeUpdateCoordinator(
            lifecycle,
            new FakeUpdateInstaller(calls));

        await coordinator.ApplyAsync(
            Candidate("0.1.1-rc.3"),
            UpdateDiagnostics(),
            CancellationToken.None);

        Assert.Equal(["stop", "install", "start"], calls);
    }

    [Fact]
    public async Task OwnedRuntimeIsRestartedWhenInstallationFails()
    {
        var calls = new List<string>();
        var lifecycle = new UpdateLifecycle(HarnessRuntimeState.RunningOwned, calls);
        using var coordinator = new DshRuntimeUpdateCoordinator(
            lifecycle,
            new FakeUpdateInstaller(calls) { Failure = new IOException("failed") });

        await Assert.ThrowsAsync<IOException>(() => coordinator.ApplyAsync(
            Candidate("0.1.1-rc.3"),
            UpdateDiagnostics(),
            CancellationToken.None));

        Assert.Equal(["stop", "install", "start"], calls);
    }

    [Fact]
    public async Task CandidateSmokeDoesNotOpenDefaultBrowser()
    {
        var process = new CapturingProcessManager();
        var verifier = new DshCandidateSmokeVerifier(process, new ConfirmingHealthMonitor());
        var candidate = new DshInstallationCandidate(
            DshInstallationSource.Private,
            "node.exe",
            "bin.js",
            DshPackageMetadata.BootstrapVersion,
            "test");

        await verifier.VerifyAsync(candidate, CancellationToken.None);

        Assert.NotNull(process.StartOptions);
        Assert.Equal("bin.js", process.StartOptions.Arguments[0]);
        Assert.Equal("web", process.StartOptions.Arguments[1]);
        Assert.Equal("--no-open", process.StartOptions.Arguments[2]);
        Assert.Equal("--port", process.StartOptions.Arguments[3]);
    }

    [Fact]
    public async Task CatalogInstallerCommitsOnlyAfterSmokeAndCleansBothTransactions()
    {
        var calls = new List<string>();
        var policy = new RecordingPolicy(Descriptor(DshPackageMetadata.BootstrapVersion), calls);
        var store = new RecordingStore(calls);
        var installer = new DshCatalogUpdateInstaller(
            new RecordingDownloader(calls),
            store,
            new RecordingNpmRunner(calls),
            new RecordingSmokeVerifier(calls),
            policy);

        await installer.InstallAsync(
            Candidate("0.1.1-rc.3"),
            "0.11.0",
            "v24.0.0",
            "node.exe",
            "npm.cmd",
            CancellationToken.None);

        Assert.Equal(
            ["download", "create", "npm", "commit", "smoke", "activate", "select", "cleanup-store", "cleanup-assets"],
            calls);
    }

    [Fact]
    public async Task SmokeFailureNeverChangesActiveOrSelectedPointers()
    {
        var calls = new List<string>();
        var policy = new RecordingPolicy(Descriptor(DshPackageMetadata.BootstrapVersion), calls);
        var installer = new DshCatalogUpdateInstaller(
            new RecordingDownloader(calls),
            new RecordingStore(calls),
            new RecordingNpmRunner(calls),
            new RecordingSmokeVerifier(calls) { Failure = new HarnessException(
                DshUpdateErrorMapper.SmokeFailed("failed")) },
            policy);

        await Assert.ThrowsAsync<HarnessException>(() => installer.InstallAsync(
            Candidate("0.1.1-rc.3"),
            "0.11.0",
            "v24.0.0",
            "node.exe",
            "npm.cmd",
            CancellationToken.None));

        Assert.DoesNotContain("activate", calls);
        Assert.DoesNotContain("select", calls);
        Assert.Equal("cleanup-store", calls[calls.Count - 2]);
        Assert.Equal("cleanup-assets", calls[calls.Count - 1]);
    }

    private static DshCatalogSnapshot Snapshot(long sequence, params DshCatalogEntry[] entries) =>
        new(1, sequence, DateTimeOffset.Now, "test", entries);

    private static DshCatalogUpdateCandidate Candidate(string version)
    {
        var entry = Entry(version);
        return new DshCatalogUpdateCandidate(
            entry,
            new DshRuntimeDescriptor(
                version,
                entry.RuntimeProtocol,
                entry.MinimumDesktopVersion,
                entry.NodeVersionRange,
                "catalog:12",
                entry.Lock.Sha256),
            12);
    }

    private static DshCatalogEntry Entry(string version) => new(
        version,
        DateTimeOffset.Now,
        DshPackageMetadata.RuntimeProtocol,
        DshPackageMetadata.MinimumDesktopVersion,
        DshPackageMetadata.SupportedNodeVersionRange,
        false,
        new DshCatalogAsset(new Uri("https://assets.example.test/package.json"), 1, new string('a', 64)),
        new DshCatalogAsset(new Uri("https://assets.example.test/package-lock.json"), 1, new string('b', 64)));

    private static DshCatalogAsset Asset(string file, byte[] bytes, string sha256) =>
        new(new Uri($"https://assets.example.test/{file}"), bytes.Length, sha256);

    private static DshRuntimeDescriptor Descriptor(string version) => new(
        version,
        DshPackageMetadata.RuntimeProtocol,
        DshPackageMetadata.MinimumDesktopVersion,
        DshPackageMetadata.SupportedNodeVersionRange,
        "test",
        new string('a', 64));

    private static DependencyDiagnosticsResult UpdateDiagnostics() => new(
        "0.11.0",
        "4.8.0",
        new DependencyCheck(DependencyStatus.Available),
        new DependencyCheck(DependencyStatus.Available, Version: DshPackageMetadata.BootstrapVersion),
        new DependencyCheck(DependencyStatus.Available, "node.exe", "v24.0.0"),
        new DependencyCheck(DependencyStatus.Available, "npx.cmd"),
        [],
        new DependencyCheck(DependencyStatus.Available, "npm.cmd"),
        DshInstallationSource.Private);

    private static string Sha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class FakeReleaseService(DshUpdateCheckResult result) : IDshReleaseService
    {
        public Task<DshUpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class CapturingProcessManager : IHarnessProcessManager
    {
        public event EventHandler<ProcessOutputEventArgs>? OutputReceived { add { } remove { } }
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited { add { } remove { } }
        public DshLaunchOptions? StartOptions { get; private set; }
        public HarnessProcessInfo? Current { get; private set; }
        public bool IsRunning => Current is not null;

        public Task<HarnessProcessInfo> StartAsync(
            DshLaunchOptions options,
            CancellationToken cancellationToken)
        {
            StartOptions = options;
            Current = new HarnessProcessInfo(42, DateTimeOffset.UtcNow, options.WorkingDirectory, null);
            return Task.FromResult(Current);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Current = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new();
    }

    private sealed class ConfirmingHealthMonitor : IHarnessHealthMonitor
    {
        public Task<HealthProbeResult> ProbeAsync(
            Uri uri,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<HealthProbeResult> WaitUntilReadyAsync(
            Func<Uri> uriProvider,
            TimeSpan startupTimeout,
            CancellationToken cancellationToken)
        {
            var uri = uriProvider();
            return Task.FromResult(new HealthProbeResult(
                HealthProbeStatus.DshConfirmed,
                uri,
                uri));
        }
    }

    private sealed class FakeCatalogService(DshCatalogLoadResult result) : IDshCatalogService
    {
        public Task<DshCatalogLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FixedPolicy(DshRuntimeDescriptor descriptor) : IDshTrustedVersionPolicy
    {
        public DshRuntimeDescriptor Bootstrap => descriptor;
        public DshRuntimeDescriptor Current => descriptor;
        public Task SelectAsync(DshRuntimeDescriptor value, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class AssetHandler(byte[] package, byte[] lockBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bytes = request.RequestUri!.AbsolutePath.EndsWith("package-lock.json", StringComparison.Ordinal)
                ? lockBytes
                : package;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeUpdateInstaller(List<string> calls) : IDshCatalogUpdateInstaller
    {
        public Exception? Failure { get; init; }

        public Task<DshInstallationCandidate> InstallAsync(
            DshCatalogUpdateCandidate target,
            string desktopVersion,
            string nodeVersion,
            string nodePath,
            string npmPath,
            CancellationToken cancellationToken)
        {
            calls.Add("install");
            if (Failure is not null)
            {
                return Task.FromException<DshInstallationCandidate>(Failure);
            }
            return Task.FromResult(new DshInstallationCandidate(
                DshInstallationSource.Private,
                nodePath,
                "bin.js",
                target.Entry.Version,
                "test"));
        }
    }

    private sealed class UpdateLifecycle(
        HarnessRuntimeState state,
        List<string> calls) : IHarnessLifecycleCoordinator
    {
        public HarnessStateSnapshot Current { get; private set; } = SnapshotFor(state);
        public event EventHandler<HarnessStateSnapshot>? StateChanged;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ApplyServiceUriAsync(Uri serviceUri, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RestartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            calls.Add("stop");
            Current = SnapshotFor(HarnessRuntimeState.Stopped);
            StateChanged?.Invoke(this, Current);
            return Task.CompletedTask;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            calls.Add("start");
            Current = SnapshotFor(HarnessRuntimeState.RunningOwned);
            StateChanged?.Invoke(this, Current);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => new();

        private static HarnessStateSnapshot SnapshotFor(HarnessRuntimeState value) => new(
            value,
            value is HarnessRuntimeState.RunningOwned or HarnessRuntimeState.RunningExternal
                ? new Uri("http://127.0.0.1:3080/")
                : null,
            value == HarnessRuntimeState.RunningOwned ? 42 : null,
            value == HarnessRuntimeState.RunningOwned,
            null,
            value.ToString(),
            DateTimeOffset.Now,
            1);
    }

    private sealed class RecordingDownloader(List<string> calls) : IDshCatalogAssetDownloader
    {
        public Task<DshDownloadedAssets> DownloadAsync(
            DshCatalogEntry entry,
            CancellationToken cancellationToken)
        {
            calls.Add("download");
            return Task.FromResult(new DshDownloadedAssets("assets", "package.json", "package-lock.json"));
        }

        public Task CleanupAsync(DshDownloadedAssets assets)
        {
            calls.Add("cleanup-assets");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStore(List<string> calls) : IPrivateDshInstallationStore
    {
        public Task<DshInstallationCandidate?> FindActiveAsync(
            string? nodePath,
            CancellationToken cancellationToken) => Task.FromResult<DshInstallationCandidate?>(null);

        public Task<PrivateDshInstallTransaction> CreateTransactionAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PrivateDshInstallTransaction> CreateTransactionAsync(
            DshRuntimeDescriptor descriptor,
            string packagePath,
            string lockPath,
            CancellationToken cancellationToken)
        {
            calls.Add("create");
            return Task.FromResult(new PrivateDshInstallTransaction(
                "staging",
                "install",
                descriptor.EvidenceSha256,
                descriptor));
        }

        public Task<DshInstallationCandidate> CommitVersionAsync(
            PrivateDshInstallTransaction transaction,
            string nodePath,
            CancellationToken cancellationToken)
        {
            calls.Add("commit");
            return Task.FromResult(new DshInstallationCandidate(
                DshInstallationSource.Private,
                nodePath,
                "bin.js",
                transaction.Version,
                transaction.InstallId));
        }

        public Task ActivateAsync(
            DshInstallationCandidate candidate,
            CancellationToken cancellationToken)
        {
            calls.Add("activate");
            return Task.CompletedTask;
        }

        public Task CleanupAsync(PrivateDshInstallTransaction transaction)
        {
            calls.Add("cleanup-store");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNpmRunner(List<string> calls) : INpmInstallRunner
    {
        public Task RunAsync(
            string npmPath,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            calls.Add("npm");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSmokeVerifier(List<string> calls) : IDshCandidateSmokeVerifier
    {
        public Exception? Failure { get; init; }

        public Task VerifyAsync(
            DshInstallationCandidate candidate,
            CancellationToken cancellationToken)
        {
            calls.Add("smoke");
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class RecordingPolicy : IDshTrustedVersionPolicy
    {
        private readonly List<string> _calls;

        public RecordingPolicy(DshRuntimeDescriptor current, List<string> calls)
        {
            Bootstrap = current;
            Current = current;
            _calls = calls;
        }

        public DshRuntimeDescriptor Bootstrap { get; }
        public DshRuntimeDescriptor Current { get; private set; }

        public Task SelectAsync(DshRuntimeDescriptor value, CancellationToken cancellationToken)
        {
            _calls.Add("select");
            Current = value;
            return Task.CompletedTask;
        }
    }
}
