using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace DeepSeekHarnessDesktop.Services;

public sealed class LogService : IDisposable
{
    private const long FileSizeLimitBytes = 10 * 1024 * 1024;
    private readonly Serilog.Core.Logger _logger;
    private readonly SerilogLoggerFactory _loggerFactory;

    public LogService(string? logDirectory = null, SensitiveDataRedactor? redactor = null)
    {
        logDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeekHarnessDesktop",
            "logs");
        Directory.CreateDirectory(logDirectory);
        var formatter = new RedactingTextFormatter(redactor ?? new SensitiveDataRedactor());
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                formatter,
                Path.Combine(logDirectory, "desktop-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 7,
                shared: false)
            .CreateLogger();
        _loggerFactory = new SerilogLoggerFactory(_logger, dispose: false);
    }

    public ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        _loggerFactory.CreateLogger(categoryName);

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logger.Dispose();
    }
}
