using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using DeepSeekHarnessDesktop.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class ChatModeTests
{
    [Fact]
    public void MainWindowStartsInCodeWithoutInitializingChat()
    {
        var chat = new FakeChatWebViewService();
        using var viewModel = CreateViewModel(chat, new FakeConfirmation(true));

        Assert.Equal(AppContentMode.Code, viewModel.CurrentMode);
        Assert.True(viewModel.IsCodeMode);
        Assert.False(viewModel.IsChatMode);
        Assert.Equal(0, chat.InitializeCount);
    }

    [Fact]
    public async Task ReturningToChatDoesNotRequestSecondInitialization()
    {
        var chat = new FakeChatWebViewService();
        using var viewModel = CreateViewModel(chat, new FakeConfirmation(true));

        await viewModel.SwitchToChatCommand.ExecuteAsync(null);
        viewModel.SwitchToCodeCommand.Execute(null);
        await viewModel.SwitchToChatCommand.ExecuteAsync(null);

        Assert.Equal(1, chat.InitializeCount);
        Assert.Equal(AppContentMode.Chat, viewModel.CurrentMode);
    }

    [Fact]
    public async Task ChatCommandsDoNotInvokeHarnessLifecycle()
    {
        var coordinator = new FakeCoordinator(RunningOwned());
        var chat = new FakeChatWebViewService();
        using var viewModel = CreateViewModel(chat, new FakeConfirmation(true), coordinator);

        await viewModel.SwitchToChatCommand.ExecuteAsync(null);
        await viewModel.ReloadPageCommand.ExecuteAsync(null);
        await viewModel.ClearChatDataCommand.ExecuteAsync(null);

        Assert.False(viewModel.RestartCommand.CanExecute(null));
        Assert.Equal(1, chat.ReloadCount);
        Assert.Equal(1, chat.ClearCount);
        Assert.Equal(0, coordinator.StartCount + coordinator.StopCount + coordinator.RestartCount);
        Assert.Equal(42, viewModel.Snapshot.ProcessId);
    }

    [Fact]
    public async Task ClearChatDataRequiresConfirmation()
    {
        var chat = new FakeChatWebViewService();
        using var viewModel = CreateViewModel(chat, new FakeConfirmation(false));
        await viewModel.SwitchToChatCommand.ExecuteAsync(null);

        await viewModel.ClearChatDataCommand.ExecuteAsync(null);

        Assert.Equal(0, chat.ClearCount);
    }

    [Theory]
    [InlineData("https://chat.deepseek.com/", ChatNavigationDecision.Embed)]
    [InlineData("https://chat.deepseek.com:443/a?x=1", ChatNavigationDecision.Embed)]
    [InlineData("HTTP://chat.deepseek.com/", ChatNavigationDecision.Reject)]
    [InlineData("https://chat.deepseek.com:444/", ChatNavigationDecision.Reject)]
    [InlineData("https://user@chat.deepseek.com/", ChatNavigationDecision.Reject)]
    [InlineData("https://chat.deepseek.com./", ChatNavigationDecision.Reject)]
    [InlineData("https://chat.deepseek.com.example.org/", ChatNavigationDecision.OpenExternal)]
    [InlineData("https://deepseek.com/", ChatNavigationDecision.OpenExternal)]
    [InlineData("http://example.org/", ChatNavigationDecision.OpenExternal)]
    [InlineData("file:///c:/windows/win.ini", ChatNavigationDecision.Reject)]
    [InlineData("javascript:alert(1)", ChatNavigationDecision.Reject)]
    public void ChatPolicyUsesExactOrigin(string value, ChatNavigationDecision expected)
    {
        Assert.Equal(expected, ChatNavigationPolicy.Decide(new Uri(value)));
    }

    [Fact]
    public void ChatPolicyDoesNotEmbedUnicodeLookalikeHost()
    {
        var target = new Uri("https://ch\u0430t.deepseek.com/");

        Assert.Equal(ChatNavigationDecision.OpenExternal, ChatNavigationPolicy.Decide(target));
    }

    [Theory]
    [InlineData(CoreWebView2WebErrorStatus.HostNameNotResolved, 0, "WEB-E312")]
    [InlineData(CoreWebView2WebErrorStatus.CertificateExpired, 0, "WEB-E313")]
    [InlineData(CoreWebView2WebErrorStatus.Unknown, 503, "WEB-E314")]
    public void ChatNavigationFailuresHaveDedicatedCodes(
        CoreWebView2WebErrorStatus status,
        int httpStatusCode,
        string expectedCode)
    {
        Assert.Equal(expectedCode, ChatErrorMapper.NavigationFailure(status, httpStatusCode).Code);
    }

    [Fact]
    public void ChatProfilePathMustStayWithinSharedUserDataFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "webview-root");
        var profile = Path.Combine(root, "EBWebView", ChatWebViewService.ProfileName);
        var outside = Path.Combine(Path.GetTempPath(), "webview-root-copy", ChatWebViewService.ProfileName);

        Assert.True(ChatWebViewService.IsWithinUserDataFolder(profile, root));
        Assert.False(ChatWebViewService.IsWithinUserDataFolder(outside, root));
    }

    [Theory]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("https://user@example.org/")]
    public void ExternalLauncherRejectsDangerousUris(string value)
    {
        var launcher = new ExternalLinkLauncher();

        Assert.Throws<ArgumentException>(() => launcher.Open(new Uri(value)));
    }

    private static MainWindowViewModel CreateViewModel(
        FakeChatWebViewService chat,
        FakeConfirmation confirmation,
        FakeCoordinator? coordinator = null) => new(
            coordinator ?? new FakeCoordinator(Stopped()),
            new FakeCodeWebViewService(),
            new FakeWorkspacePicker(),
            new RecentLogBuffer(),
            new AppSettings { WorkspacePath = Path.GetTempPath() },
            Diagnostics(),
            null,
            chat,
            confirmation);

    private static HarnessStateSnapshot Stopped() => new(
        HarnessRuntimeState.Stopped, null, null, false, null, "stopped", DateTimeOffset.UtcNow, 1);

    private static HarnessStateSnapshot RunningOwned() => new(
        HarnessRuntimeState.RunningOwned,
        new Uri("http://127.0.0.1:3080/"),
        42,
        true,
        null,
        "running",
        DateTimeOffset.UtcNow,
        3);

    private static DependencyDiagnosticsResult Diagnostics() => new(
        "0.4.0",
        "8.0.0",
        new DependencyCheck(DependencyStatus.Missing),
        new DependencyCheck(DependencyStatus.Missing),
        new DependencyCheck(DependencyStatus.Missing),
        new DependencyCheck(DependencyStatus.Missing),
        []);

    private sealed class FakeChatWebViewService : IChatWebViewService
    {
        public ChatPageSnapshot Current { get; private set; } = ChatPageSnapshot.Initial;
        public bool IsInitialized { get; private set; }
        public event EventHandler<ChatPageSnapshot>? StateChanged;
        public int InitializeCount { get; private set; }
        public int ReloadCount { get; private set; }
        public int ClearCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            InitializeCount++;
            IsInitialized = true;
            SetReady();
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken cancellationToken)
        {
            ReloadCount++;
            SetReady();
            return Task.CompletedTask;
        }

        public Task ClearBrowsingDataAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            SetReady();
            return Task.CompletedTask;
        }

        private void SetReady()
        {
            Current = new ChatPageSnapshot(
                ChatPageState.Ready, null, "ready", DateTimeOffset.UtcNow, Current.Generation + 1);
            StateChanged?.Invoke(this, Current);
        }
    }

    private sealed class FakeCodeWebViewService : ICodeWebViewService
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShowLocalStateAsync(HarnessRuntimeState state, HarnessError? error, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCoordinator(HarnessStateSnapshot snapshot) : IHarnessLifecycleCoordinator
    {
        public HarnessStateSnapshot Current { get; } = snapshot;
        public event EventHandler<HarnessStateSnapshot>? StateChanged { add { } remove { } }
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

    private sealed class FakeConfirmation(bool result) : IUserConfirmationService
    {
        public bool ConfirmServiceRestart(Uri currentUri, Uri newUri) => result;
        public bool ConfirmDshDownload() => result;
        public bool ConfirmClearChatData() => result;
    }

    private sealed class FakeWorkspacePicker : IWorkspacePicker
    {
        public string? Pick(string currentPath) => null;
    }
}
