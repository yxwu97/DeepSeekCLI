namespace DeepSeekHarnessDesktop.Models;

public sealed record HarnessProcessInfo(
    int ProcessId,
    DateTimeOffset StartedAt,
    string WorkingDirectory,
    Uri? ReportedUri);
