namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshBrowserSession
{
    void Begin(Uri origin);
    void CaptureOutput(string line);
    Uri? GetAuthenticationUri(Uri origin);
    void Clear();
}
