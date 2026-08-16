using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
