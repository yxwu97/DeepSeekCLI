using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshCommandResolver
{
    Task<DshLaunchOptions> ResolveAsync(AppSettings settings, CancellationToken cancellationToken);
}
