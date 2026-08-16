namespace DeepSeekHarnessDesktop.Models;

public sealed record HarnessStateSnapshot(
    HarnessRuntimeState State,
    Uri? ServiceUri,
    int? ProcessId,
    bool IsOwned,
    HarnessError? Error,
    string StatusMessage,
    DateTimeOffset ChangedAt,
    long Generation);
