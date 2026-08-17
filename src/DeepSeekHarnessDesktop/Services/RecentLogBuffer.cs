using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using Microsoft.Extensions.Logging;

namespace DeepSeekHarnessDesktop.Services;

public sealed class RecentLogBuffer : IRecentLogBuffer
{
    public const int Capacity = 1000;
    private readonly object _sync = new();
    private readonly Queue<ProcessOutputLine> _lines = new(Capacity);
    private readonly SensitiveDataRedactor _redactor;
    private readonly ILogger<RecentLogBuffer>? _logger;

    public RecentLogBuffer(
        SensitiveDataRedactor? redactor = null,
        ILogger<RecentLogBuffer>? logger = null)
    {
        _redactor = redactor ?? new SensitiveDataRedactor();
        _logger = logger;
    }

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
        var normalized = OutputLineProcessor.Normalize(line.Text);
        if (normalized is null)
        {
            return;
        }

        var safeLine = line with { Text = _redactor.Redact(normalized) };
        lock (_sync)
        {
            if (_lines.Count == Capacity)
            {
                _lines.Dequeue();
            }
            _lines.Enqueue(safeLine);
        }
        if (safeLine.Source == ProcessOutputSource.Desktop)
        {
            _logger?.LogInformation(new EventId(1200), "{Message}", safeLine.Text);
        }
        LineAdded?.Invoke(this, safeLine);
    }

    public void AddDesktop(string text) =>
        Add(new ProcessOutputLine(DateTimeOffset.Now, ProcessOutputSource.Desktop, text));
}
