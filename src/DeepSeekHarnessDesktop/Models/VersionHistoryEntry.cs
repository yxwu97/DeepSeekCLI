namespace DeepSeekHarnessDesktop.Models;

public sealed record VersionHistoryEntry(
    string Version,
    string Date,
    IReadOnlyList<string> Changes);
