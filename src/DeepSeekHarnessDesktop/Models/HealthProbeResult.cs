namespace DeepSeekHarnessDesktop.Models;

public enum HealthProbeStatus
{
    DshConfirmed,
    Unreachable,
    ReachableUnknown,
    ExternalRedirect,
    InvalidUri,
}

public sealed record HealthProbeResult(
    HealthProbeStatus Status,
    Uri RequestedUri,
    Uri? FinalUri = null,
    string? Detail = null);
