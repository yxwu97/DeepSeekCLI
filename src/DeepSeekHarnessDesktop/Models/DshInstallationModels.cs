namespace DeepSeekHarnessDesktop.Models;

public enum DshInstallationSource
{
    None,
    GlobalPath,
    Private,
    NpxCache,
}

public sealed record DshInstallationCandidate(
    DshInstallationSource Source,
    string ExecutablePath,
    string? EntryPointPath,
    string Version,
    string? InstallId = null);

public sealed record DshDiscoveryResult(
    DshInstallationCandidate? Candidate,
    string? NodePath,
    string? NpmPath,
    string? NpxPath)
{
    public bool HasInstalledDsh => Candidate is not null;
    public bool CanPrepare => !string.IsNullOrWhiteSpace(NodePath)
        && !string.IsNullOrWhiteSpace(NpmPath);
    public bool RequiresPreparation => Candidate is null && CanPrepare;
}
