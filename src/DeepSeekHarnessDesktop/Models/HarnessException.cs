namespace DeepSeekHarnessDesktop.Models;

public sealed class HarnessException(HarnessError error) : Exception(error.TechnicalMessage, error.Exception)
{
    public HarnessError Error { get; } = error;
}
