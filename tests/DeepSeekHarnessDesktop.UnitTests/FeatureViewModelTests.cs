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
        var expected = new DshUpdateCheckResult("0.1.0", DateTimeOffset.Now);
        var viewModel = new AboutViewModel(
            new FakeDiagnosticsService(LaunchableDiagnostics()),
            new FakeReleaseService(expected),
            new FakeLinkLauncher(),
            new FakeVersionHistoryProvider(),
            LaunchableDiagnostics());

        await viewModel.CheckUpdateCommand.ExecuteAsync(null);

        Assert.Same(expected, viewModel.UpdateResult);
        Assert.Contains("不锁定版本", viewModel.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutProjectGitHubCommandUsesFixedOfficialResource()
    {
        var linkLauncher = new FakeLinkLauncher();
        var viewModel = new AboutViewModel(
            new FakeDiagnosticsService(LaunchableDiagnostics()),
            new FakeReleaseService(new DshUpdateCheckResult("0.1.0", DateTimeOffset.Now)),
            linkLauncher,
            new FakeVersionHistoryProvider(),
            LaunchableDiagnostics());

        viewModel.OpenDesktopGitHubCommand.Execute(null);

        Assert.Equal(OfficialResource.DesktopGitHub, linkLauncher.LastResource);
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

    [Fact]
    public void InstallationGuideManualActionsUseFixedCommandAndValidatedWorkspace()
    {
        var clipboard = new FakeClipboard();
        var terminal = new FakeTerminalLauncher();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath(), StartupTimeoutSeconds = 45 };
        using var viewModel = CreateInstallationGuide(
            new FakeCoordinator(Stopped()),
            new FakeConfirmation(true),
            settings,
            clipboard,
            terminal);

        viewModel.CopyManualInstallCommand.Execute(null);
        viewModel.OpenPowerShellCommand.Execute(null);

        Assert.Equal("npx @deepseek-ai/dsh web", clipboard.Text);
        Assert.Equal(settings.WorkspacePath, terminal.WorkingDirectory);
        Assert.EndsWith("/ 00:45", viewModel.ElapsedText, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallationGuideManualActionFailureRemainsRetryable()
    {
        var clipboard = new FakeClipboard { Failure = new InvalidOperationException("busy") };
        using var viewModel = CreateInstallationGuide(
            new FakeCoordinator(Stopped()),
            new FakeConfirmation(true),
            clipboard: clipboard);

        viewModel.CopyManualInstallCommand.Execute(null);

        Assert.Contains("失败", viewModel.StageMessage, StringComparison.Ordinal);
        Assert.True(viewModel.CopyManualInstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task InstallationGuideSettlesStageAndTotalTimingOnStateTransitions()
    {
        var time = new ManualTimeProvider();
        var coordinator = new FakeCoordinator(Stopped());
        coordinator.OnStart = () =>
        {
            time.Advance(TimeSpan.FromSeconds(7));
            coordinator.Set(Starting("正在创建 DSH 进程"));
            time.Advance(TimeSpan.FromSeconds(3));
            coordinator.Set(RunningOwned());
        };
        var logs = new RecentLogBuffer();
        using var viewModel = CreateInstallationGuide(
            coordinator,
            new FakeConfirmation(true),
            logBuffer: logs,
            timeProvider: time);

        await viewModel.DownloadAndStartCommand.ExecuteAsync(null);

        var text = string.Join('\n', logs.Snapshot().Select(line => line.Text));
        Assert.Contains("耗时 00:07", text, StringComparison.Ordinal);
        Assert.Contains("总耗时 00:10", text, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    private static InstallationGuideViewModel CreateInstallationGuide(
        FakeCoordinator coordinator,
        FakeConfirmation confirmation,
        AppSettings? settings = null,
        IClipboardService? clipboard = null,
        ITerminalLauncher? terminalLauncher = null,
        IRecentLogBuffer? logBuffer = null,
        TimeProvider? timeProvider = null)
    {
        var diagnostics = LaunchableDiagnostics();
        return new InstallationGuideViewModel(
            new FakeDiagnosticsService(diagnostics),
            coordinator,
            logBuffer ?? new RecentLogBuffer(),
            new FakeLinkLauncher(),
            confirmation,
            diagnostics,
            settings,
            clipboard,
            terminalLauncher,
            timeProvider);
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

    private static HarnessStateSnapshot Starting(string message) => new(
        HarnessRuntimeState.Starting, null, null, true, null, message, DateTimeOffset.Now, 2);

    private sealed class FakeCoordinator(HarnessStateSnapshot snapshot) : IHarnessLifecycleCoordinator
    {
        public HarnessStateSnapshot Current { get; private set; } = snapshot;
        public event EventHandler<HarnessStateSnapshot>? StateChanged;
        public int StartCount { get; private set; }
        public int ApplyCount { get; private set; }
        public Action? OnStart { get; set; }
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) { StartCount++; OnStart?.Invoke(); return Task.CompletedTask; }
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
        public OfficialResource? LastResource { get; private set; }
        public void Open(OfficialResource resource) => LastResource = resource;
        public void Open(Uri uri) { }
    }

    private sealed class FakeVersionHistoryProvider : IVersionHistoryProvider
    {
        public IReadOnlyList<VersionHistoryEntry> GetEntries() =>
            [new("0.0.0", "2026-01-01", ["测试版本记录"])];
    }

    private sealed class FakeConfirmation(bool result) : IUserConfirmationService
    {
        public bool ConfirmServiceRestart(Uri currentUri, Uri newUri) => result;
        public bool ConfirmDshDownload() => result;
        public bool ConfirmClearChatData() => result;
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public Exception? Failure { get; init; }
        public void SetText(string text)
        {
            if (Failure is not null)
            {
                throw Failure;
            }
            Text = text;
        }
    }

    private sealed class FakeTerminalLauncher : ITerminalLauncher
    {
        public string? WorkingDirectory { get; private set; }
        public void OpenPowerShell(string workingDirectory) => WorkingDirectory = workingDirectory;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
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
