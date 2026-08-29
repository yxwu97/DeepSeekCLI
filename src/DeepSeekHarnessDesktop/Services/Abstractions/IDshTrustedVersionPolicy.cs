using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshTrustedVersionPolicy
{
    DshRuntimeDescriptor Bootstrap { get; }
    DshRuntimeDescriptor Current { get; }
    Task SelectAsync(DshRuntimeDescriptor descriptor, CancellationToken cancellationToken);
}
