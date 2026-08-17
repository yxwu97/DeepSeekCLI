namespace DeepSeekHarnessDesktop.Models;

public enum ProcessOutputSource
{
    Desktop,
    StandardOutput,
    StandardError,
}

public sealed record ProcessOutputLine(
    DateTimeOffset Timestamp,
    ProcessOutputSource Source,
    string Text)
{
    public string DisplayText => $"[{Timestamp:HH:mm:ss}] [{SourceLabel}] {Text}";

    private string SourceLabel => Source switch
    {
        ProcessOutputSource.Desktop => "desktop",
        ProcessOutputSource.StandardError => "stderr",
        _ => "stdout",
    };
}

public sealed class ProcessOutputEventArgs(ProcessOutputLine line) : EventArgs
{
    public ProcessOutputLine Line { get; } = line;
}

public sealed class ProcessExitedEventArgs(int processId, int exitCode) : EventArgs
{
    public int ProcessId { get; } = processId;
    public int ExitCode { get; } = exitCode;
}
