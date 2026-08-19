using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekHarnessDesktop.Services;

public sealed class ChatWebViewService : IChatWebViewService, IAsyncDisposable
{
    public const string ProfileName = "Chat";

    private readonly AppSettings _settings;
    private readonly IWebViewEnvironmentProvider _environmentProvider;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly HashSet<ulong> _cancelledNavigationIds = [];
    private WebView2? _browser;
    private ChatPageSnapshot _current = ChatPageSnapshot.Initial;
    private long _generation;
    private bool _initialized;
    private bool _recoveryAttempted;
    private bool _disposed;

    public ChatWebViewService(
        AppSettings settings,
        IWebViewEnvironmentProvider environmentProvider,
        IExternalLinkLauncher linkLauncher)
    {
        _settings = settings;
        _environmentProvider = environmentProvider;
        _linkLauncher = linkLauncher;
    }

    public ChatPageSnapshot Current => _current;
    public bool IsInitialized => _initialized;
    public event EventHandler<ChatPageSnapshot>? StateChanged;

    public void Attach(WebView2 browser)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChatWebViewService));
        if (_browser is not null && !ReferenceEquals(_browser, browser))
        {
            throw new InvalidOperationException("A Chat WebView2 control is already attached.");
        }
        _browser = browser;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        await _operationGate.WaitAsync(linkedCts.Token);
        try
        {
            if (_initialized)
            {
                if (Current.State == ChatPageState.Failed)
                {
                    await NavigateEntryAsync(linkedCts.Token);
                }
                return;
            }

            var generation = Interlocked.Increment(ref _generation);
            SetSnapshot(ChatPageState.Initializing, null, "正在加载 DeepSeek Chat", generation);
            var browser = GetBrowser();
            var environment = await _environmentProvider.GetAsync(linkedCts.Token);
            await WebViewDispatcher.InvokeAsync(browser.Dispatcher, async () =>
            {
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.ProfileName = ProfileName;
                options.IsInPrivateModeEnabled = false;
                await browser.EnsureCoreWebView2Async(environment, options);
                linkedCts.Token.ThrowIfCancellationRequested();
                Configure(browser.CoreWebView2);
                ValidateProfile(browser.CoreWebView2.Profile);
                browser.ZoomFactor = _settings.WebView.ZoomFactor;
                _initialized = true;
                browser.CoreWebView2.Navigate(ChatNavigationPolicy.EntryUri.AbsoluteUri);
            }, linkedCts.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetSnapshot(ChatPageState.Failed, new HarnessError(
                "WEB-E311",
                "DeepSeek Chat 初始化失败",
                exception.Message,
                true,
                exception), "初始化失败", _generation);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        await _operationGate.WaitAsync(linkedCts.Token);
        try
        {
            await NavigateEntryAsync(linkedCts.Token);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ClearBrowsingDataAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        await _operationGate.WaitAsync(linkedCts.Token);
        var generation = Interlocked.Increment(ref _generation);
        try
        {
            SetSnapshot(ChatPageState.ClearingData, null, "正在清除 Chat 登录信息", generation);
            var browser = GetBrowser();
            await WebViewDispatcher.InvokeAsync(browser.Dispatcher, async () =>
            {
                browser.CoreWebView2.Stop();
                await browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
                linkedCts.Token.ThrowIfCancellationRequested();
                SetSnapshot(ChatPageState.Initializing, null, "登录信息已清除，正在重新加载", generation);
                browser.CoreWebView2.Navigate(ChatNavigationPolicy.EntryUri.AbsoluteUri);
            }, linkedCts.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetSnapshot(ChatPageState.Failed, new HarnessError(
                "WEB-E316",
                "无法清除 Chat 登录信息",
                exception.Message,
                true,
                exception), "清除失败", generation);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task NavigateEntryAsync(CancellationToken cancellationToken)
    {
        var generation = Interlocked.Increment(ref _generation);
        SetSnapshot(ChatPageState.Initializing, null, "正在加载 DeepSeek Chat", generation);
        var browser = GetBrowser();
        await WebViewDispatcher.InvokeAsync(browser.Dispatcher, () =>
        {
            browser.CoreWebView2.Navigate(ChatNavigationPolicy.EntryUri.AbsoluteUri);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    private void Configure(CoreWebView2 core)
    {
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Profile.IsPasswordAutosaveEnabled = true;
        core.Profile.IsGeneralAutofillEnabled = true;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.DownloadStarting += OnDownloadStarting;
        core.ProcessFailed += OnProcessFailed;
    }

    private void ValidateProfile(CoreWebView2Profile profile)
    {
        if (!profile.ProfileName.Equals(ProfileName, StringComparison.Ordinal)
            || !IsWithinUserDataFolder(profile.ProfilePath, _environmentProvider.UserDataFolder))
        {
            throw new InvalidOperationException("Chat WebView2 profile is outside the expected user data folder.");
        }
    }

    public static bool IsWithinUserDataFolder(string profilePath, string userDataFolder)
    {
        var root = PathCompatibility.TrimEndingDirectorySeparator(userDataFolder)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(profilePath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var target))
        {
            var decision = ChatNavigationPolicy.Decide(target);
            if (decision == ChatNavigationDecision.Embed)
            {
                SetSnapshot(ChatPageState.Initializing, null, "正在加载 DeepSeek Chat", _generation);
                return;
            }

            e.Cancel = true;
            _cancelledNavigationIds.Add(e.NavigationId);
            if (decision == ChatNavigationDecision.OpenExternal)
            {
                OpenExternal(target);
            }
            return;
        }

        e.Cancel = true;
        _cancelledNavigationIds.Add(e.NavigationId);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_cancelledNavigationIds.Remove(e.NavigationId))
        {
            return;
        }

        if (e.IsSuccess)
        {
            _recoveryAttempted = false;
            SetSnapshot(ChatPageState.Ready, null, "DeepSeek Chat 已就绪", _generation);
            return;
        }

        var error = ChatErrorMapper.NavigationFailure(e.WebErrorStatus, e.HttpStatusCode);
        SetSnapshot(ChatPageState.Failed, error, "加载失败", _generation);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target))
        {
            return;
        }

        switch (ChatNavigationPolicy.Decide(target))
        {
            case ChatNavigationDecision.Embed:
                _browser?.CoreWebView2.Navigate(target.AbsoluteUri);
                break;
            case ChatNavigationDecision.OpenExternal:
                OpenExternal(target);
                break;
        }
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
    }

    private static void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (!_recoveryAttempted && _browser?.CoreWebView2 is { } core)
        {
            _recoveryAttempted = true;
            SetSnapshot(ChatPageState.Initializing, null, "正在恢复 DeepSeek Chat", _generation);
            core.Reload();
            return;
        }

        SetSnapshot(ChatPageState.Failed, new HarnessError(
            "WEB-E315",
            "DeepSeek Chat 页面进程异常",
            $"WebView2 process failure: {e.ProcessFailedKind}.",
            true), "页面进程异常", _generation);
    }

    private void OpenExternal(Uri target)
    {
        try
        {
            _linkLauncher.Open(target);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetSnapshot(ChatPageState.Failed, new HarnessError(
                "WEB-E318",
                "无法打开外部链接",
                exception.Message,
                true,
                exception), "外部链接打开失败", _generation);
        }
    }

    private void SetSnapshot(ChatPageState state, HarnessError? error, string message, long generation)
    {
        if (generation < _current.Generation || _disposed)
        {
            return;
        }
        _current = new ChatPageSnapshot(state, error, message, DateTimeOffset.UtcNow, generation);
        StateChanged?.Invoke(this, _current);
    }

    private WebView2 GetBrowser() => _browser
        ?? throw new InvalidOperationException("Chat WebView2 control is not attached.");

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetimeCts.Cancel();
        await _operationGate.WaitAsync();
        try
        {
            if (_browser is { } browser)
            {
                await WebViewDispatcher.InvokeAsync(browser.Dispatcher, () =>
                {
                    if (_initialized && browser.CoreWebView2 is { } core)
                    {
                        core.NavigationStarting -= OnNavigationStarting;
                        core.NavigationCompleted -= OnNavigationCompleted;
                        core.NewWindowRequested -= OnNewWindowRequested;
                        core.PermissionRequested -= OnPermissionRequested;
                        core.DownloadStarting -= OnDownloadStarting;
                        core.ProcessFailed -= OnProcessFailed;
                    }
                    browser.Dispose();
                    return Task.CompletedTask;
                }, CancellationToken.None);
                _browser = null;
            }
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
            _lifetimeCts.Dispose();
        }
    }
}
