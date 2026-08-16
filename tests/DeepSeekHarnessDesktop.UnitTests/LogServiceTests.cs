using DeepSeekHarnessDesktop.Services;
using Microsoft.Extensions.Logging;
using System.Collections;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class LogServiceTests
{
    [Fact]
    public void RedactorRemovesBearerEnvironmentAndQuerySecrets()
    {
        var environment = new Hashtable
        {
            ["DEEPSEEK_API_KEY"] = "api-value-123",
            ["SESSION_TOKEN"] = "token-value-456",
            ["ORDINARY_VALUE"] = "keep-this",
        };
        var redactor = new SensitiveDataRedactor(environment);

        var result = redactor.Redact(
            "Authorization: Bearer bearer-value "
            + "api-value-123 token-value-456 keep-this "
            + "https://localhost/?key=query-key&token=query-token&secret=query-secret&name=visible");

        Assert.DoesNotContain("bearer-value", result);
        Assert.DoesNotContain("api-value-123", result);
        Assert.DoesNotContain("token-value-456", result);
        Assert.DoesNotContain("query-key", result);
        Assert.DoesNotContain("query-token", result);
        Assert.DoesNotContain("query-secret", result);
        Assert.Contains("keep-this", result);
        Assert.Contains("name=visible", result);
    }

    [Fact]
    public void FileLogRollerWritesEventIdAndOnlyRedactedText()
    {
        using var directory = new TemporaryDirectory();
        var environment = new Hashtable { ["API_KEY"] = "file-secret-value" };
        using (var service = new LogService(directory.Path, new SensitiveDataRedactor(environment)))
        {
            var logger = service.CreateLogger<LogServiceTests>();
            logger.LogInformation(
                new EventId(1001),
                "Starting with {Header} at {Uri} and {Key}",
                "Authorization: Bearer bearer-secret",
                "http://127.0.0.1/?token=url-secret",
                "file-secret-value");
        }

        var logPath = Assert.Single(Directory.GetFiles(directory.Path, "desktop-*.log"));
        var content = File.ReadAllText(logPath);
        Assert.Contains("1001", content);
        Assert.Contains("[REDACTED]", content);
        Assert.DoesNotContain("bearer-secret", content);
        Assert.DoesNotContain("url-secret", content);
        Assert.DoesNotContain("file-secret-value", content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-log-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
