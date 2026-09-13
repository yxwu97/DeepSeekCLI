namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IDshBrowserSession
{
    void Begin(Uri origin);
    void CaptureOutput(string line);
    bool TryBeginExternal(Uri origin, string authenticationLink);
    void ClearExternal();
    Uri? GetAuthenticationUri(Uri origin);
    void Clear();
}
