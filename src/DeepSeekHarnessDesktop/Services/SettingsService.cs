using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using Microsoft.Extensions.Logging;
using DeepSeekHarnessDesktop.Utilities;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DeepSeekHarnessDesktop.Services;

public sealed class SettingsService : ISettingsService, IDisposable
{
    public const int CurrentSchemaVersion = 2;
    private const string SettingsFileName = "settings.json";
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly string _temporaryPath;
    private readonly ILogger<SettingsService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsService(ILogger<SettingsService>? logger = null)
        : this(GetDefaultSettingsDirectory(), logger)
    {
    }

    public SettingsService(string settingsDirectory, ILogger<SettingsService>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        var directory = Path.GetFullPath(settingsDirectory);
        _settingsPath = Path.Combine(directory, SettingsFileName);
        _backupPath = _settingsPath + ".bak";
        _temporaryPath = _settingsPath + ".tmp";
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return CreateDefaults();
            }

            try
            {
                var settings = await ReadAndValidateAsync(_settingsPath, cancellationToken);
                _logger?.LogInformation(1100, "Loaded settings schema {SchemaVersion}.", settings.SchemaVersion);
                return settings;
            }
            catch (Exception exception) when (IsRecoverableLoadFailure(exception))
            {
                _logger?.LogWarning(1101, exception, "Primary settings file is invalid; attempting backup recovery.");
            }

            if (File.Exists(_backupPath))
            {
                try
                {
                    var recovered = await ReadAndValidateAsync(_backupPath, cancellationToken);
                    await WriteFileAsync(_temporaryPath, recovered, cancellationToken);
                    File.Move(_temporaryPath, _settingsPath, true);
                    _logger?.LogWarning(1102, "Recovered settings from backup.");
                    return recovered;
                }
                catch (Exception exception) when (IsRecoverableLoadFailure(exception))
                {
                    _logger?.LogError(1103, exception, "Settings backup recovery failed; using defaults (CFG-E401).");
                }
            }

            return CreateDefaults();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await WriteFileAsync(_temporaryPath, settings, cancellationToken);
            if (File.Exists(_settingsPath))
            {
                File.Replace(_temporaryPath, _settingsPath, _backupPath, true);
            }
            else
            {
                File.Move(_temporaryPath, _settingsPath);
            }
            _logger?.LogInformation(1110, "Saved settings schema {SchemaVersion}.", settings.SchemaVersion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogError(1111, exception, "Unable to save settings (CFG-E402).");
            throw new HarnessException(new HarnessError(
                "CFG-E402",
                "无法保存设置",
                exception.Message,
                true,
                exception));
        }
        finally
        {
            TryDeleteTemporaryFile();
            _gate.Release();
        }
    }

    public static AppSettings CreateDefaults() => new();

    internal static void Validate(AppSettings settings)
    {
        if (settings.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported settings schema version: {settings.SchemaVersion}.");
        }
        if (string.IsNullOrWhiteSpace(settings.WorkspacePath) || !Path.IsPathFullyQualified(settings.WorkspacePath))
        {
            throw new InvalidDataException("Workspace path must be absolute.");
        }
        if (!ServiceUriValidator.TryNormalize(settings.ServiceUri, out var normalized, out var uriError))
        {
            throw new InvalidDataException(uriError);
        }
        settings.ServiceUri = normalized;
        if (settings.StartupTimeoutSeconds is < 5 or > 300)
        {
            throw new InvalidDataException("Startup timeout must be between 5 and 300 seconds.");
        }
        if (settings.Window.Width < 820 || settings.Window.Height < 600)
        {
            throw new InvalidDataException("Window dimensions are below the supported minimum.");
        }
        if (settings.WebView.ZoomFactor is < 0.5 or > 2.0)
        {
            throw new InvalidDataException("WebView zoom factor must be between 0.5 and 2.0.");
        }
        if (!Enum.IsDefined(settings.Launch.Mode))
        {
            throw new InvalidDataException("Launch mode is invalid.");
        }
    }

    private async Task<AppSettings> ReadAndValidateAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Settings document is empty.");
        if (document is not JsonObject root
            || root["schemaVersion"]?.GetValue<int?>() is not { } schemaVersion
            || schemaVersion <= 0)
        {
            throw new InvalidDataException("Settings schemaVersion is missing or invalid.");
        }

        Migrate(root, schemaVersion);
        var settings = root.Deserialize<AppSettings>(_jsonOptions)
            ?? throw new InvalidDataException("Settings document is empty.");
        Validate(settings);
        return settings;
    }

    private static void Migrate(JsonObject root, int schemaVersion)
    {
        if (schemaVersion == 1)
        {
            if (root["startupTimeoutSeconds"]?.GetValue<int?>() is 60)
            {
                root["startupTimeoutSeconds"] = 300;
            }
            root["schemaVersion"] = CurrentSchemaVersion;
            return;
        }

        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported settings schema version: {schemaVersion}.");
        }
    }

    private async Task WriteFileAsync(string path, AppSettings settings, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(true);
    }

    private static bool IsRecoverableLoadFailure(Exception exception) =>
        exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException;

    private void TryDeleteTemporaryFile()
    {
        try
        {
            File.Delete(_temporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetDefaultSettingsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeepSeekHarnessDesktop");

    public void Dispose() => _gate.Dispose();
}
