using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;

namespace DeepSeekHarnessDesktop.Services;

public sealed class RecentLogBuffer : IRecentLogBuffer
{
    public const int Capacity = 1000;
    private readonly object _sync = new();
    private readonly Queue<ProcessOutputLine> _lines = new(Capacity);

    public event EventHandler<ProcessOutputLine>? LineAdded;

    public IReadOnlyList<ProcessOutputLine> Snapshot()
    {
        lock (_sync)
        {
            return _lines.ToArray();
        }
    }

    public void Add(ProcessOutputLine line)
    {
        lock (_sync)
        {
            if (_lines.Count == Capacity)
            {
                _lines.Dequeue();
            }
            _lines.Enqueue(line);
        }
        LineAdded?.Invoke(this, line);
    }
}
