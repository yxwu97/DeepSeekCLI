using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDependencyDiagnosticsService
{
    Task<DependencyDiagnosticsResult> DiagnoseAsync(CancellationToken cancellationToken);
}
