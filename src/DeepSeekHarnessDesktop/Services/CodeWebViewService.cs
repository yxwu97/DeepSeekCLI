using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekHarnessDesktop.Services;

public sealed class CodeWebViewService : ICodeWebViewService, IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly IWebViewEnvironmentProvider _environmentProvider;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private WebView2? _browser;
    private Uri? _allowedServiceUri;
    private bool _initialized;
    private bool _recoveryAttempted;
    private bool _disposed;

    public CodeWebViewService(
        AppSettings settings,
        IWebViewEnvironmentProvider environmentProvider,
        IExternalLinkLauncher linkLauncher)
    {
        _settings = settings;
        _environmentProvider = environmentProvider;
        _linkLauncher = linkLauncher;
    }

    public void Attach(WebView2 browser)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_browser is not null && !ReferenceEquals(_browser, browser))
        {
            throw new InvalidOperationException("A Code WebView2 control is already attached.");
        }
        _browser = browser;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        await _operationGate.WaitAsync(linkedCts.Token);
        try
        {
            await InitializeCoreAsync(linkedCts.Token);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task NavigateAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!ServiceUriValidator.TryNormalize(uri, out uri, out _))
        {
            throw new HarnessException(new HarnessError(
                "DSH-E202", "服务地址无效", "Code navigation URI was rejected.", true));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        await _operationGate.WaitAsync(linkedCts.Token);
        try
        {
            await InitializeCoreAsync(linkedCts.Token);
            var browser = GetBrowser();
            await WebViewDispatcher.InvokeAsync(browser.Dispatcher, () =>
            {
                _allowedServiceUri = uri;
                browser.CoreWebView2.Navigate(uri.AbsoluteUri);
                return Task.CompletedTask;
            }, linkedCts.Token);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        await _operationGate.WaitAsync(linkedCts.Token);
        try
        {
            await InitializeCoreAsync(linkedCts.Token);
            var browser = GetBrowser();
            await WebViewDispatcher.InvokeAsync(browser.Dispatcher, () =>
            {
                browser.CoreWebView2.Reload();
                return Task.CompletedTask;
            }, linkedCts.Token);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task ShowLocalStateAsync(
        HarnessRuntimeState state,
        HarnessError? error,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public static bool IsSameOrigin(Uri candidate, Uri allowed) =>
        string.Equals(candidate.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)
        && candidate.Port == allowed.Port;

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            var browser = GetBrowser();
            var environment = await _environmentProvider.GetAsync(cancellationToken);
            await WebViewDispatcher.InvokeAsync(browser.Dispatcher, async () =>
            {
                await browser.EnsureCoreWebView2Async(environment);
                cancellationToken.ThrowIfCancellationRequested();
                Configure(browser.CoreWebView2);
                browser.ZoomFactor = _settings.WebView.ZoomFactor;
                _initialized = true;
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new HarnessException(new HarnessError(
                "WEB-E301",
                "WebView2 Runtime 不可用",
                exception.Message,
                false,
                exception));
        }
    }

    private void Configure(CoreWebView2 core)
    {
        core.Settings.AreDefaultContextMenusEnabled = true;
#if DEBUG
        core.Settings.AreDevToolsEnabled = _settings.WebView.AllowDevTools;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_allowedServiceUri is not null
            && Uri.TryCreate(e.Uri, UriKind.Absolute, out var target)
            && IsSameOrigin(target, _allowedServiceUri))
        {
            return;
        }

        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (_recoveryAttempted || _browser?.CoreWebView2 is null)
        {
            return;
        }
        _recoveryAttempted = true;
        _browser.CoreWebView2.Reload();
    }

    private void OpenExternal(string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var target)
            && target.UserInfo.Length == 0
            && target.Scheme is "http" or "https")
        {
            _linkLauncher.Open(target);
        }
    }

    private WebView2 GetBrowser() => _browser
        ?? throw new InvalidOperationException("Code WebView2 control is not attached.");

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
                        core.NewWindowRequested -= OnNewWindowRequested;
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
