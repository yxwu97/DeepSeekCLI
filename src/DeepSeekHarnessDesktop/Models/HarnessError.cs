namespace DeepSeekHarnessDesktop.Models;

public sealed record HarnessError(
    string Code,
    string UserMessage,
    string TechnicalMessage,
    bool IsRetryable,
    Exception? Exception = null);
