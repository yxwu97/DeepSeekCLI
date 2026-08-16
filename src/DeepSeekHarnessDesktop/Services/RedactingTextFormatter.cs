using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace DeepSeekHarnessDesktop.Services;

internal sealed class RedactingTextFormatter : ITextFormatter
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] [{EventId}] {Message:lj}{NewLine}{Exception}";
    private readonly MessageTemplateTextFormatter _formatter = new(OutputTemplate);
    private readonly SensitiveDataRedactor _redactor;

    public RedactingTextFormatter(SensitiveDataRedactor redactor)
    {
        _redactor = redactor;
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _formatter.Format(logEvent, buffer);
        output.Write(_redactor.Redact(buffer.ToString()));
    }
}
