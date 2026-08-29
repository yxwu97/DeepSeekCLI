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

public sealed record DshVersionProbeResult(
    bool Succeeded,
    string? Version,
    string? Detail = null);

public sealed record DshCandidateRejection(
    string? ActualVersion,
    string Detail);

public sealed record DshDiscoveryResult(
    DshInstallationCandidate? Candidate,
    string? NodePath,
    string? NpmPath,
    string? NpxPath,
    DshCandidateRejection? RejectedGlobalDsh = null)
{
    public bool HasInstalledDsh => Candidate is not null;
    public bool CanPrepare => !string.IsNullOrWhiteSpace(NodePath)
        && !string.IsNullOrWhiteSpace(NpmPath);
    public bool RequiresPreparation => Candidate is null && CanPrepare;
}
