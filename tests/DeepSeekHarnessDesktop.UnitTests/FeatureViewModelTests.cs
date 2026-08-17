using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.ViewModels;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class FeatureViewModelTests
{
    [Fact]
    public void InstallationGuideConstructionHasNoProcessOrNetworkSideEffects()
    {
        var coordinator = new FakeCoordinator(Stopped());
        using var viewModel = CreateInstallationGuide(coordinator, new FakeConfirmation(false));

        Assert.Equal(0, coordinator.StartCount);
        Assert.Equal(0, coordinator.ApplyCount);
        Assert.True(viewModel.CanLaunch);
    }

    [Fact]
    public async Task InstallationGuideRequiresConfirmationBeforeNpxStart()
    {
        var coordinator = new FakeCoordinator(Stopped());
        using var viewModel = CreateInstallationGuide(coordinator, new FakeConfirmation(false));

        await viewModel.DownloadAndStartCommand.ExecuteAsync(null);

        Assert.Equal(0, coordinator.StartCount);
        Assert.Contains("取消", viewModel.StageMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsConnectionTestDoesNotPersistOrApply()
    {
        var coordinator = new FakeCoordinator(Stopped());
        var health = new FakeHealthMonitor(HealthProbeStatus.DshConfirmed);
        using var viewModel = new SettingsViewModel(
            coordinator, health, new FakeConfirmation(true), new AppSettings { WorkspacePath = Path.GetTempPath() });
        viewModel.ServiceAddress = "http://localhost:43140/path";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(1, health.ProbeCount);
        Assert.Equal(0, coordinator.ApplyCount);
        Assert.Contains("连接成功", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsOwnedApplyStopsWhenRestartConfirmationIsDeclined()
    {
        var coordinator = new FakeCoordinator(RunningOwned());
        using var viewModel = new SettingsViewModel(
            coordinator,
            new FakeHealthMonitor(HealthProbeStatus.DshConfirmed),
            new FakeConfirmation(false),
            new AppSettings { WorkspacePath = Path.GetTempPath() });
        viewModel.ServiceAddress = "http://127.0.0.1:43141/";

        await viewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(0, coordinator.ApplyCount);
    }

    [Fact]
    public async Task AboutUpdateCheckOnlyUpdatesPresentationResult()
    {
        var expected = new DshUpdateCheckResult("0.1.0-rc.6", "0.1.0", true, DateTimeOffset.Now);
        var viewModel = new AboutViewModel(
            new FakeDiagnosticsService(LaunchableDiagnostics()),
            new FakeReleaseService(expected),
            new FakeLinkLauncher(),
            LaunchableDiagnostics());

        await viewModel.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Same(expected, viewModel.UpdateResult);
        Assert.Contains("不会自动切换", viewModel.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallationGuideActivationHidesRunningWebViewPriority()
    {
        var coordinator = new FakeCoordinator(RunningOwned());
        using var guide = CreateInstallationGuide(coordinator, new FakeConfirmation(true));
        using var main = new MainWindowViewModel(
            coordinator,
            new FakeNavigation(),
            new FakeWorkspacePicker(),
            new RecentLogBuffer(),
            new AppSettings { WorkspacePath = Path.GetTempPath() },
            LaunchableDiagnostics(),
            guide);

        guide.Activate();

        Assert.True(main.IsInstallationGuideActive);
        Assert.False(main.IsWebViewVisible);
    }

    private static InstallationGuideViewModel CreateInstallationGuide(
        FakeCoordinator coordinator,
        FakeConfirmation confirmation)
    {
        var diagnostics = LaunchableDiagnostics();
        return new InstallationGuideViewModel(
            new FakeDiagnosticsService(diagnostics),
            coordinator,
            new RecentLogBuffer(),
            new FakeLinkLauncher(),
            confirmation,
            diagnostics);
    }

    private static DependencyDiagnosticsResult LaunchableDiagnostics() => new(
        "0.2.0",
        "8.0.0",
        new DependencyCheck(DependencyStatus.Available, Version: "140.0"),
        new DependencyCheck(DependencyStatus.Missing),
        new DependencyCheck(DependencyStatus.Available, Version: "v24"),
        new DependencyCheck(DependencyStatus.Available, Path: "npx.cmd"),
        []);

    private static HarnessStateSnapshot Stopped() => new(
        HarnessRuntimeState.Stopped, null, null, false, null, "stopped", DateTimeOffset.Now, 1);

    private static HarnessStateSnapshot RunningOwned() => new(
        HarnessRuntimeState.RunningOwned,
        new Uri("http://127.0.0.1:3080/"),
        42,
        true,
        null,
        "running",
        DateTimeOffset.Now,
        1);

    private sealed class FakeCoordinator(HarnessStateSnapshot snapshot) : IHarnessLifecycleCoordinator
    {
        public HarnessStateSnapshot Current { get; private set; } = snapshot;
        public event EventHandler<HarnessStateSnapshot>? StateChanged;
        public int StartCount { get; private set; }
        public int ApplyCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) { StartCount++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RestartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ApplyServiceUriAsync(Uri serviceUri, CancellationToken cancellationToken) { ApplyCount++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Set(HarnessStateSnapshot value)
        {
            Current = value;
            StateChanged?.Invoke(this, value);
        }
    }

    private sealed class FakeHealthMonitor(HealthProbeStatus status) : IHarnessHealthMonitor
    {
        public int ProbeCount { get; private set; }
        public Task<HealthProbeResult> ProbeAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ProbeCount++;
            return Task.FromResult(new HealthProbeResult(status, uri, status == HealthProbeStatus.DshConfirmed ? uri : null));
        }

        public Task<HealthProbeResult> WaitUntilReadyAsync(Func<Uri> uriProvider, TimeSpan startupTimeout, CancellationToken cancellationToken) =>
            ProbeAsync(uriProvider(), startupTimeout, cancellationToken);
    }

    private sealed class FakeDiagnosticsService(DependencyDiagnosticsResult result) : IDependencyDiagnosticsService
    {
        public Task<DependencyDiagnosticsResult> DiagnoseAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FakeReleaseService(DshUpdateCheckResult result) : IDshReleaseService
    {
        public Task<DshUpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FakeLinkLauncher : IExternalLinkLauncher
    {
        public void Open(OfficialResource resource) { }
        public void Open(Uri uri) { }
    }

    private sealed class FakeConfirmation(bool result) : IUserConfirmationService
    {
        public bool ConfirmServiceRestart(Uri currentUri, Uri newUri) => result;
        public bool ConfirmDshDownload() => result;
        public bool ConfirmClearChatData() => result;
    }

    private sealed class FakeNavigation : ICodeWebViewService
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShowLocalStateAsync(HarnessRuntimeState state, HarnessError? error, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeWorkspacePicker : IWorkspacePicker
    {
        public string? Pick(string currentPath) => null;
    }
}
