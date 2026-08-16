namespace DeepSeekHarnessDesktop.Models;

public enum ProcessOutputSource
{
    StandardOutput,
    StandardError,
}

public sealed record ProcessOutputLine(
    DateTimeOffset Timestamp,
    ProcessOutputSource Source,
    string Text);

public sealed class ProcessOutputEventArgs(ProcessOutputLine line) : EventArgs
{
    public ProcessOutputLine Line { get; } = line;
}

public sealed class ProcessExitedEventArgs(int processId, int exitCode) : EventArgs
{
    public int ProcessId { get; } = processId;
    public int ExitCode { get; } = exitCode;
}
