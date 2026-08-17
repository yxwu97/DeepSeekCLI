using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task MissingFileReturnsDefaults()
    {
        using var directory = new TemporaryDirectory();
        using var service = new SettingsService(directory.Path);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(SettingsService.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(new Uri("http://127.0.0.1:3080/"), settings.ServiceUri);
        Assert.Equal(300, settings.StartupTimeoutSeconds);
        Assert.True(Path.IsPathFullyQualified(settings.WorkspacePath));
    }

    [Fact]
    public async Task SaveUsesCamelCaseAndCreatesSingleBackup()
    {
        using var directory = new TemporaryDirectory();
        using var service = new SettingsService(directory.Path);
        var first = CreateSettings("first");
        var second = CreateSettings("second");

        await service.SaveAsync(first, CancellationToken.None);
        await service.SaveAsync(second, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(directory.Path, "settings.json"));
        var backup = await File.ReadAllTextAsync(Path.Combine(directory.Path, "settings.json.bak"));
        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("second", json);
        Assert.Contains("first", backup);
        Assert.False(File.Exists(Path.Combine(directory.Path, "settings.json.tmp")));
    }

    [Fact]
    public async Task CorruptPrimaryRecoversBackupAndRepairsPrimary()
    {
        using var directory = new TemporaryDirectory();
        using var service = new SettingsService(directory.Path);
        var first = CreateSettings("backup");
        var second = CreateSettings("primary");
        await service.SaveAsync(first, CancellationToken.None);
        await service.SaveAsync(second, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "settings.json"), "{broken");

        var recovered = await service.LoadAsync(CancellationToken.None);
        var reloaded = await service.LoadAsync(CancellationToken.None);

        Assert.EndsWith("backup", recovered.WorkspacePath);
        Assert.Equal(recovered.WorkspacePath, reloaded.WorkspacePath);
        Assert.Equal(recovered.ServiceUri, reloaded.ServiceUri);
        Assert.Equal(recovered.AutoStart, reloaded.AutoStart);
        Assert.Equal(recovered.Launch.Arguments, reloaded.Launch.Arguments);
    }

    [Fact]
    public async Task CorruptPrimaryAndBackupReturnDefaults()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "settings.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "settings.json.bak"), "not-json");
        using var service = new SettingsService(directory.Path);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(SettingsService.CreateDefaults(), settings);
    }

    [Theory]
    [InlineData(60, 300)]
    [InlineData(45, 45)]
    [InlineData(300, 300)]
    public async Task VersionOneSettingsMigrateBeforeValidation(int sourceTimeout, int expectedTimeout)
    {
        using var directory = new TemporaryDirectory();
        var workspace = Path.Combine(directory.Path, "workspace");
        Directory.CreateDirectory(workspace);
        var json = $$"""
            {
              "schemaVersion": 1,
              "workspacePath": "{{JsonEscape(workspace)}}",
              "serviceUri": "http://127.0.0.1:3080/",
              "autoStart": false,
              "startupTimeoutSeconds": {{sourceTimeout}}
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "settings.json"), json);
        using var service = new SettingsService(directory.Path);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(SettingsService.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(expectedTimeout, settings.StartupTimeoutSeconds);
        Assert.Equal(workspace, settings.WorkspacePath);
    }

    [Fact]
    public async Task CorruptPrimaryRecoversAndMigratesVersionOneBackup()
    {
        using var directory = new TemporaryDirectory();
        var workspace = Path.Combine(directory.Path, "backup-workspace");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "settings.json"), "{broken");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "settings.json.bak"), $$"""
            {
              "schemaVersion": 1,
              "workspacePath": "{{JsonEscape(workspace)}}",
              "serviceUri": "http://127.0.0.1:3080/",
              "autoStart": false,
              "startupTimeoutSeconds": 60
            }
            """);
        using var service = new SettingsService(directory.Path);

        var settings = await service.LoadAsync(CancellationToken.None);
        var repaired = await File.ReadAllTextAsync(Path.Combine(directory.Path, "settings.json"));

        Assert.Equal(2, settings.SchemaVersion);
        Assert.Equal(300, settings.StartupTimeoutSeconds);
        Assert.Equal(workspace, settings.WorkspacePath);
        Assert.Contains("\"schemaVersion\": 2", repaired, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4, 1.0, 1280, 820)]
    [InlineData(60, 0.4, 1280, 820)]
    [InlineData(60, 1.0, 819, 820)]
    [InlineData(301, 1.0, 1280, 820)]
    public async Task InvalidSettingsAreRejected(int timeout, double zoom, double width, double height)
    {
        using var directory = new TemporaryDirectory();
        using var service = new SettingsService(directory.Path);
        var settings = CreateSettings("invalid") with
        {
            StartupTimeoutSeconds = timeout,
            Window = new WindowSettings { Width = width, Height = height },
            WebView = new WebViewSettings { ZoomFactor = zoom },
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SaveAsync(settings, CancellationToken.None));
    }

    [Fact]
    public async Task UnsupportedSchemaFallsBackWithoutOverwritingSource()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        var path = Path.Combine(directory.Path, "settings.json");
        const string source = "{\"schemaVersion\":99,\"workspacePath\":\"C:\\\\work\"}";
        await File.WriteAllTextAsync(path, source);
        using var service = new SettingsService(directory.Path);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(SettingsService.CreateDefaults(), settings);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
    }

    private static AppSettings CreateSettings(string leaf) => new()
    {
        WorkspacePath = Path.Combine(Path.GetTempPath(), leaf),
        AutoStart = false,
    };

    private static string JsonEscape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-settings-tests", Guid.NewGuid().ToString("N"));
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
