using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IRuntimeHealthWatcher
{
    Task<RuntimeHealthLost?> WatchAsync(Uri uri, long generation, CancellationToken cancellationToken);
}
