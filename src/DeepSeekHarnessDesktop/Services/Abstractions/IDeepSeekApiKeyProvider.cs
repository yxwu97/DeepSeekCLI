namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDeepSeekApiKeyProvider
{
    Task<string?> GetCurrentAsync(CancellationToken cancellationToken);
}
