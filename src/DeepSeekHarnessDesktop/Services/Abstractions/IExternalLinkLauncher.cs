namespace DeepSeekHarnessDesktop.Services.Abstractions;

public enum OfficialResource
{
    NodeDownload,
    DshDocumentation,
    NpmPackage,
    DesktopGitHub,
}

public interface IExternalLinkLauncher
{
    void Open(OfficialResource resource);
    void Open(Uri uri);
}
