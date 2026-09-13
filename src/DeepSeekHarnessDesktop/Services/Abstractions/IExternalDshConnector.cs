namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IExternalDshConnector
{
    Task ConnectExternalAsync(string authenticationLink, CancellationToken cancellationToken);
}
