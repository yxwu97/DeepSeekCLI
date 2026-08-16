using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IRecentLogBuffer
{
    event EventHandler<ProcessOutputLine>? LineAdded;
    IReadOnlyList<ProcessOutputLine> Snapshot();
    void Add(ProcessOutputLine line);
}
