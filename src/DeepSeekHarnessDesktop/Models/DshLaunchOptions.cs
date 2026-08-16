namespace DeepSeekHarnessDesktop.Models;

public sealed record DshLaunchOptions
{
    public required string ExecutablePath { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string WorkingDirectory { get; init; }
    public required Uri FallbackUri { get; init; }
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>();
}
