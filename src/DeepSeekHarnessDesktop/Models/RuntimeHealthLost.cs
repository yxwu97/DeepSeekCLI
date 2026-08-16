namespace DeepSeekHarnessDesktop.Models;

public sealed record RuntimeHealthLost(
    long Generation,
    HealthProbeResult LastProbe,
    HarnessError? Error = null);
