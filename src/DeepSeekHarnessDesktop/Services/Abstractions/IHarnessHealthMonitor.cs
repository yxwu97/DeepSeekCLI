using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IHarnessHealthMonitor
{
    Task<HealthProbeResult> ProbeAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken);
    Task<HealthProbeResult> WaitUntilReadyAsync(Func<Uri> uriProvider, TimeSpan startupTimeout, CancellationToken cancellationToken);
}
