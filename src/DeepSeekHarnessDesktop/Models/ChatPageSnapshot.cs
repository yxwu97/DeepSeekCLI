namespace DeepSeekHarnessDesktop.Models;

public enum ChatPageState
{
    NotInitialized,
    Initializing,
    Ready,
    Failed,
    ClearingData,
}

public sealed record ChatPageSnapshot(
    ChatPageState State,
    HarnessError? Error,
    string StatusMessage,
    DateTimeOffset UpdatedAt,
    long Generation)
{
    public static ChatPageSnapshot Initial { get; } = new(
        ChatPageState.NotInitialized,
        null,
        "尚未加载 DeepSeek Chat",
        DateTimeOffset.UtcNow,
        0);
}
