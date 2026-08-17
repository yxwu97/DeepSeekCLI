namespace DeepSeekHarnessDesktop.Services.Abstractions;

public enum OfficialResource
{
    NodeDownload,
    DshDocumentation,
    NpmPackage,
}

public interface IExternalLinkLauncher
{
    void Open(OfficialResource resource);
    void Open(Uri uri);
}
