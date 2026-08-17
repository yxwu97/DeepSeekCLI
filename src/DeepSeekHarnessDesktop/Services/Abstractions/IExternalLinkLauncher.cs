namespace DeepSeekHarnessDesktop.Services.Abstractions;

public enum OfficialResource
{
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
