namespace DeepSeekHarnessDesktop.Models;

public sealed record DshUpdateCheckResult(
    string VerifiedVersion,
    string? LatestVersion,
    bool IsUpdateAvailable,
    DateTimeOffset CheckedAt,
    string? ErrorMessage = null)
{
    public bool Succeeded => ErrorMessage is null;
}
