using Microsoft.Web.WebView2.Core;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IWebViewEnvironmentProvider
{
    string UserDataFolder { get; }
    Task<CoreWebView2Environment> GetAsync(CancellationToken cancellationToken);
}
