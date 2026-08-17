using DeepSeekHarnessDesktop.Services.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekHarnessDesktop.Services;

public sealed class WebViewEnvironmentProvider : IWebViewEnvironmentProvider
{
    private readonly object _sync = new();
    private Task<CoreWebView2Environment>? _creationTask;

    public WebViewEnvironmentProvider()
    {
        UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeekHarnessDesktop",
            "WebView2");
    }

    public string UserDataFolder { get; }

    public async Task<CoreWebView2Environment> GetAsync(CancellationToken cancellationToken)
    {
        Task<CoreWebView2Environment> creationTask;
        lock (_sync)
        {
            if (_creationTask is null || _creationTask.IsFaulted || _creationTask.IsCanceled)
            {
                _creationTask = CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
            }
            creationTask = _creationTask;
        }

        return await creationTask.WaitAsync(cancellationToken);
    }
}
