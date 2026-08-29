using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshVersionProbe
{
    Task<DshVersionProbeResult> ProbeAsync(
        string executablePath,
        CancellationToken cancellationToken);
}
