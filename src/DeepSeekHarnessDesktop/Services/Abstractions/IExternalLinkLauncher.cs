namespace DeepSeekHarnessDesktop.Services.Abstractions;

public enum OfficialResource
{
    WebView2Download,
    NodeDownload,
    DshDocumentation,
    NpmPackage,
    DesktopGitHub,
    DeepSeekTopUp,
}

public interface IExternalLinkLauncher
{
    void Open(OfficialResource resource);
    void Open(Uri uri);
}
