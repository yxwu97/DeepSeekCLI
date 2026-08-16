namespace DeepSeekHarnessDesktop.Models;

public sealed record DeepSeekBalanceInfo(
    string Currency,
    decimal TotalBalance,
    decimal GrantedBalance,
    decimal ToppedUpBalance);

public sealed record DeepSeekAccountSnapshot(
    bool IsAvailable,
    IReadOnlyList<DeepSeekBalanceInfo> Balances);

public sealed record DeepSeekAccountError(
    string Code,
    string UserMessage,
    string TechnicalMessage,
    bool IsRetryable,
    Exception? Exception = null);

public sealed class DeepSeekAccountException(DeepSeekAccountError error)
    : Exception(error.TechnicalMessage, error.Exception)
{
    public DeepSeekAccountError Error { get; } = error;
}
