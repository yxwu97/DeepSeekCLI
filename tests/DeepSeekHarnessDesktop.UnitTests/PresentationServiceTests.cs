using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.ViewModels;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class PresentationServiceTests
{
    [Fact]
    public void RecentLogBufferKeepsLatestThousandLines()
    {
        var buffer = new RecentLogBuffer();
        for (var index = 0; index < 1_005; index++)
        {
            buffer.Add(new ProcessOutputLine(DateTimeOffset.UtcNow, ProcessOutputSource.StandardOutput, index.ToString()));
        }

        var lines = buffer.Snapshot();

        Assert.Equal(RecentLogBuffer.Capacity, lines.Count);
        Assert.Equal("5", lines[0].Text);
        Assert.Equal("1004", lines[^1].Text);
    }

    [Theory]
    [InlineData("http://127.0.0.1:3080/a", "http://127.0.0.1:3080/", true)]
    [InlineData("http://127.0.0.1:3081/", "http://127.0.0.1:3080/", false)]
    [InlineData("http://localhost:3080/", "http://127.0.0.1:3080/", false)]
    [InlineData("https://127.0.0.1:3080/", "http://127.0.0.1:3080/", false)]
    public void WebViewOriginIncludesSchemeHostAndPort(string candidate, string allowed, bool expected)
    {
        Assert.Equal(expected, CodeWebViewService.IsSameOrigin(new Uri(candidate), new Uri(allowed)));
    }

    [Fact]
    public async Task ReloadCommandDoesNotInvokeLifecycleOperationsOrChangePid()
    {
        var snapshot = new HarnessStateSnapshot(
            HarnessRuntimeState.RunningOwned,
            new Uri("http://127.0.0.1:3080/"),
            42,
            true,
            null,
            "running",
            DateTimeOffset.UtcNow,
            3);
        var coordinator = new FakeCoordinator(snapshot);
        var navigation = new FakeNavigation();
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath() };
        var viewModel = new MainWindowViewModel(
            coordinator,
            navigation,
            new FakeWorkspacePicker(null),
            new RecentLogBuffer(),
            settings,
            CreateDiagnostics());

        await viewModel.ReloadPageCommand.ExecuteAsync(null);

        Assert.Equal(1, navigation.ReloadCount);
        Assert.Equal(0, coordinator.StartCount + coordinator.StopCount + coordinator.RestartCount);
        Assert.Equal(42, viewModel.Snapshot.ProcessId);
    }

    [Fact]
    public void WorkspaceSelectionUpdatesStructuredSettingsWhileStopped()
    {
        var snapshot = new HarnessStateSnapshot(
            HarnessRuntimeState.Stopped, null, null, false, null, "stopped", DateTimeOffset.UtcNow, 1);
        var coordinator = new FakeCoordinator(snapshot);
        var selected = Path.GetPathRoot(Environment.SystemDirectory)!;
        var settings = new AppSettings { WorkspacePath = Path.GetTempPath() };
        var viewModel = new MainWindowViewModel(
            coordinator,
            new FakeNavigation(),
            new FakeWorkspacePicker(selected),
            new RecentLogBuffer(),
            settings,
            CreateDiagnostics());

        viewModel.SelectWorkspaceCommand.Execute(null);

        Assert.Equal(selected, viewModel.WorkspacePath);
        Assert.Equal(selected, settings.WorkspacePath);
    }

    private static DependencyDiagnosticsResult CreateDiagnostics() => new(
        "0.1.1",
        "8.0.0",
        null,
        null,
        null,
        "0.1.0-rc.6",
        []);

    private sealed class FakeCoordinator(HarnessStateSnapshot snapshot) : IHarnessLifecycleCoordinator
    {
        public HarnessStateSnapshot Current { get; } = snapshot;
        public event EventHandler<HarnessStateSnapshot>? StateChanged
        {
            add { }
            remove { }
        }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int RestartCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) { StartCount++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { StopCount++; return Task.CompletedTask; }
        public Task RestartAsync(CancellationToken cancellationToken) { RestartCount++; return Task.CompletedTask; }
        public Task ApplyServiceUriAsync(Uri serviceUri, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeNavigation : ICodeWebViewService
    {
        public int ReloadCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken cancellationToken) { ReloadCount++; return Task.CompletedTask; }
        public Task ShowLocalStateAsync(HarnessRuntimeState state, HarnessError? error, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeWorkspacePicker(string? result) : IWorkspacePicker
    {
        public string? Pick(string currentPath) => result;
    }
}
