using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Windows;

namespace DeepSeekHarnessDesktop.Phase0Validation;

internal static class DshWebViewSmoke
{
    public static async Task VerifyAsync(Uri origin, IDshBrowserSession session, string profileRoot)
    {
        await VerifySessionAsync(origin, session, profileRoot);
        var external = new DshBrowserSession();
        var link = session.GetAuthenticationUri(origin);
        if (link is null || !external.TryBeginExternal(origin, link.AbsoluteUri))
            throw new InvalidOperationException("The smoke DSH did not provide a valid external authentication link.");
        try
        {
            using var monitor = new HarnessHealthMonitor(external);
            var probe = await monitor.ProbeAsync(origin, TimeSpan.FromSeconds(5), CancellationToken.None);
            if (probe.Status != HealthProbeStatus.DshConfirmed)
                throw new InvalidOperationException("External authentication did not confirm the smoke DSH identity.");
            await VerifySessionAsync(origin, external, Path.Combine(profileRoot, "external"));
            Console.Out.WriteLine("PASS: Explicit external DSH link authenticated through an independent HTTP session and Code profile.");
        }
        finally { external.ClearExternal(); }
    }

    private static async Task VerifySessionAsync(Uri origin, IDshBrowserSession session, string profileRoot)
    {
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileRoot);
        var browser = new WebView2();
        var window = new Window { Title = "DSH Code WebView2 validation", Width = 1000, Height = 700, Content = browser };
        await using var service = new CodeWebViewService(
            new AppSettings(), new FixedEnvironment(environment, profileRoot), new RejectExternalLinks(), session);
        service.Attach(browser);
        window.Show();
        try
        {
            await service.InitializeAsync(CancellationToken.None);
            await VerifyNavigationAsync(browser, origin, () => service.NavigateAsync(origin, CancellationToken.None));
            await VerifyNavigationAsync(browser, origin, () => service.ReloadAsync(CancellationToken.None));
            var output = Path.Combine(Environment.CurrentDirectory, "output", "validation");
            Directory.CreateDirectory(output);
            using var screenshot = File.Create(Path.Combine(output, "dsh-code-webview.png"));
            await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            Console.Out.WriteLine("PASS: Code WebView2 authenticated, reached the clean DSH root and reloaded with its own cookie.");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task VerifyNavigationAsync(WebView2 browser, Uri origin, Func<Task> navigate)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess && args.HttpStatusCode == 200
                && browser.CoreWebView2.Source == origin.AbsoluteUri
                && browser.CoreWebView2.DocumentTitle == "DeepSeek Harness") completion.TrySetResult();
            else completion.TrySetException(new InvalidOperationException("Code WebView2 did not complete authenticated DSH navigation."));
        }
        browser.NavigationCompleted += Completed;
        try
        {
            await navigate();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            browser.NavigationCompleted -= Completed;
        }
    }

    private sealed class FixedEnvironment(CoreWebView2Environment environment, string root) : IWebViewEnvironmentProvider
    {
        public string UserDataFolder => root;
        public Task<CoreWebView2Environment> GetAsync(CancellationToken cancellationToken) => Task.FromResult(environment);
    }

    private sealed class RejectExternalLinks : IExternalLinkLauncher
    {
        public void Open(OfficialResource resource) => throw new InvalidOperationException("Unexpected external resource request.");
        public void Open(Uri uri) => throw new InvalidOperationException("Unexpected external navigation during DSH authentication.");
    }
}
