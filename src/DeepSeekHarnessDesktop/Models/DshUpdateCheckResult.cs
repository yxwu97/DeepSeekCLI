namespace DeepSeekHarnessDesktop.Models;

public sealed record DshUpdateCheckResult(
    string? LatestVersion,
    DateTimeOffset CheckedAt,
    string? ErrorMessage = null)
{
    public bool Succeeded => ErrorMessage is null;
}
