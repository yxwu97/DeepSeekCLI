using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Diagnostics;

namespace DeepSeekHarnessDesktop.Services;

public sealed class ExternalLinkLauncher : IExternalLinkLauncher
{
    private static readonly IReadOnlyDictionary<OfficialResource, Uri> Resources =
        new Dictionary<OfficialResource, Uri>
        {
            [OfficialResource.WebView2Download] = new("https://developer.microsoft.com/en-us/microsoft-edge/webview2/"),
            [OfficialResource.NodeDownload] = new("https://nodejs.org/en/download"),
            [OfficialResource.DshDocumentation] = new("https://github.com/deepseek-ai/DeepSeek-Harness"),
            [OfficialResource.NpmPackage] = new("https://www.npmjs.com/package/@deepseek-ai/dsh"),
            [OfficialResource.DesktopGitHub] = new("https://github.com/yxwu97/DeepSeekCLI"),
            [OfficialResource.DeepSeekTopUp] = new("https://platform.deepseek.com/top_up"),
        };

    public void Open(OfficialResource resource)
    {
        if (!Resources.TryGetValue(resource, out var uri))
        {
            throw new ArgumentOutOfRangeException(nameof(resource));
        }

        Open(uri);
    }

    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || uri.UserInfo.Length != 0
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Only absolute HTTP(S) URIs without user information are allowed.", nameof(uri));
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
