using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Diagnostics;
using System.Windows.Threading;

namespace DeepSeekHarnessDesktop.Services;

public sealed class WebViewNavigationService : IWebViewNavigationService
{
    private readonly AppSettings _settings;
    private WebView2? _browser;
    private Uri? _allowedServiceUri;
    private bool _initialized;
    private bool _recoveryAttempted;

    public WebViewNavigationService(AppSettings? settings = null)
    {
        _settings = settings ?? new AppSettings();
    }

    public void Attach(WebView2 browser)
    {
        if (_browser is not null && !ReferenceEquals(_browser, browser))
        {
            throw new InvalidOperationException("A WebView2 control is already attached.");
        }
        _browser = browser;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var browser = GetBrowser();
        if (_initialized)
        {
            return;
        }

        try
        {
            await InvokeAsync(browser.Dispatcher, async () =>
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekHarnessDesktop",
                    "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                await browser.EnsureCoreWebView2Async(environment);
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

    public async Task NavigateAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!IsAllowedServiceUri(uri))
        {
            throw new HarnessException(new HarnessError(
                "DSH-E202", "服务地址无效", $"Navigation rejected: {uri}", true));
        }

        await InitializeAsync(cancellationToken);
        var browser = GetBrowser();
        await InvokeAsync(browser.Dispatcher, () =>
        {
            _allowedServiceUri = uri;
            browser.CoreWebView2.Navigate(uri.AbsoluteUri);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var browser = GetBrowser();
        await InvokeAsync(browser.Dispatcher, () =>
        {
            browser.CoreWebView2.Reload();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task ShowLocalStateAsync(
        HarnessRuntimeState state,
        HarnessError? error,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public static bool IsSameOrigin(Uri candidate, Uri allowed) =>
        string.Equals(candidate.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)
        && candidate.Port == allowed.Port;

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
        if (_allowedServiceUri is null
            || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var target)
            || !IsSameOrigin(target, _allowedServiceUri))
        {
            e.Cancel = true;
            OpenExternal(e.Uri);
        }
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

    private static void OpenExternal(string? uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var target)
            && target.Scheme is "http" or "https")
        {
            Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private static bool IsAllowedServiceUri(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.IsLoopback
        && uri.Scheme is "http" or "https"
        && string.IsNullOrEmpty(uri.UserInfo);

    private WebView2 GetBrowser() => _browser ?? throw new InvalidOperationException("WebView2 control is not attached.");

    private static async Task InvokeAsync(
        Dispatcher dispatcher,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dispatcher.CheckAccess())
        {
            await action();
        }
        else
        {
            await dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
        }
    }
}
