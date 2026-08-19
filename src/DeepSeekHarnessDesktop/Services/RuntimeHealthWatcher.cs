using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;

namespace DeepSeekHarnessDesktop.Services;

public sealed class RuntimeHealthWatcher(
    IHarnessHealthMonitor healthMonitor,
    TimeSpan? interval = null,
    int unreachableThreshold = 3) : IRuntimeHealthWatcher
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(5);

    public async Task<RuntimeHealthLost?> WatchAsync(
        Uri uri,
        long generation,
        CancellationToken cancellationToken)
    {
        var unreachableCount = 0;
        while (true)
        {
            await Task.Delay(_interval, cancellationToken);
            var result = await healthMonitor.ProbeAsync(uri, TimeSpan.FromSeconds(2), cancellationToken);
            switch (result.Status)
            {
                case HealthProbeStatus.DshConfirmed:
                    unreachableCount = 0;
                    break;
                case HealthProbeStatus.Unreachable:
                    unreachableCount++;
                    if (unreachableCount >= unreachableThreshold)
                    {
                        return new RuntimeHealthLost(generation, result);
                    }
                    break;
                case HealthProbeStatus.ReachableUnknown:
                    return new RuntimeHealthLost(generation, result, new HarnessError(
                        "DSH-E205", "原 DSH 已不可用，地址上检测到其他服务", result.Detail ?? string.Empty, true));
                case HealthProbeStatus.ExternalRedirect:
                    return new RuntimeHealthLost(generation, result, new HarnessError(
                        "DSH-E204", "服务重定向到不允许的地址", result.Detail ?? string.Empty, false));
                case HealthProbeStatus.InvalidUri:
                    return new RuntimeHealthLost(generation, result, new HarnessError(
                        "DSH-E202", "服务地址无效", result.Detail ?? string.Empty, true));
            }
        }

    }
}
