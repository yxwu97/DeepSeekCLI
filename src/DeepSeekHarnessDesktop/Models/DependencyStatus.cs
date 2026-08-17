namespace DeepSeekHarnessDesktop.Models;

public enum DependencyStatus
{
    Available,
    Missing,
    Unusable,
}

public sealed record DependencyCheck(
    DependencyStatus Status,
    string? Path = null,
    string? Version = null,
    string? Detail = null);
