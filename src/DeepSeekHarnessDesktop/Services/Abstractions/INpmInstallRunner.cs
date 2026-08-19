namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface INpmInstallRunner
{
    Task RunAsync(string npmPath, string workingDirectory, CancellationToken cancellationToken);
}
