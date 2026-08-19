using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Text;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DeepSeekApiKeyProvider : IDeepSeekApiKeyProvider
{
    private const string ApiKeyName = "DEEPSEEK_API_KEY";

    private readonly AppSettings _settings;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _defaultDshHome;

    public DeepSeekApiKeyProvider(AppSettings settings)
        : this(
            settings,
            Environment.GetEnvironmentVariable,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh"))
    {
    }

    public DeepSeekApiKeyProvider(
        AppSettings settings,
        Func<string, string?> getEnvironmentVariable,
        string defaultDshHome)
    {
        _settings = settings;
        _getEnvironmentVariable = getEnvironmentVariable;
        _defaultDshHome = defaultDshHome;
    }

    public async Task<string?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inherited = Normalize(_getEnvironmentVariable(ApiKeyName));
        if (inherited is not null)
        {
            return inherited;
        }

        var dshHome = ResolveDshHome();
        var managed = await ReadCredentialFileAsync(
            Path.Combine(dshHome, ".credentials.yaml"),
            cancellationToken);
        if (managed is not null)
        {
            return managed;
        }

        var project = await ReadEnvironmentFileAsync(
            Path.Combine(_settings.WorkspacePath, ".env"),
            cancellationToken);
        if (project is not null)
        {
            return project;
        }

        return await ReadEnvironmentFileAsync(
            Path.Combine(dshHome, ".env"),
            cancellationToken);
    }

    private string ResolveDshHome()
    {
        var configured = _getEnvironmentVariable("DSH_HOME");
        return string.IsNullOrWhiteSpace(configured)
            ? _defaultDshHome
            : Path.GetFullPath(configured!.Trim());
    }

    private static async Task<string?> ReadCredentialFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var lines = await TryReadAllLinesAsync(path, cancellationToken);
        if (lines is null)
        {
            return null;
        }

        var found = false;
        string? result = null;
        foreach (var line in lines)
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator < 0
                || !string.Equals(line.Substring(0, separator).Trim(), ApiKeyName, StringComparison.Ordinal))
            {
                continue;
            }

            if (found)
            {
                return null;
            }

            found = true;
            result = ParseScalar(line.Substring(separator + 1));
        }

        return Normalize(result);
    }

    private static async Task<string?> ReadEnvironmentFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var lines = await TryReadAllLinesAsync(path, cancellationToken);
        if (lines is null)
        {
            return null;
        }

        string? result = null;
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.TrimStart();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line.Substring(7).TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator < 0
                || !string.Equals(line.Substring(0, separator).Trim(), ApiKeyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result = ParseScalar(line.Substring(separator + 1));
        }

        return Normalize(result);
    }

    private static async Task<string[]?> TryReadAllLinesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() => File.ReadAllLines(path), cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ParseScalar(string source)
    {
        var value = source.Trim();
        if (value.Length == 0 || value is "|" or ">" or "~")
        {
            return null;
        }

        if (value[0] == '\'')
        {
            return ParseSingleQuoted(value);
        }

        if (value[0] == '"')
        {
            return ParseDoubleQuoted(value);
        }

        var comment = FindInlineComment(value);
        return (comment < 0 ? value : value.Substring(0, comment)).TrimEnd();
    }

    private static string? ParseSingleQuoted(string value)
    {
        var result = new StringBuilder();
        for (var index = 1; index < value.Length; index++)
        {
            if (value[index] != '\'')
            {
                result.Append(value[index]);
                continue;
            }

            if (index + 1 < value.Length && value[index + 1] == '\'')
            {
                result.Append('\'');
                index++;
                continue;
            }

            return HasOnlyTrailingComment(value.Substring(index + 1)) ? result.ToString() : null;
        }

        return null;
    }

    private static string? ParseDoubleQuoted(string value)
    {
        var escaped = false;
        for (var index = 1; index < value.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (value[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (value[index] != '"')
            {
                continue;
            }

            if (!HasOnlyTrailingComment(value.Substring(index + 1)))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<string>(value.Substring(0, index + 1));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    private static int FindInlineComment(string value)
    {
        for (var index = 1; index < value.Length; index++)
        {
            if (value[index] == '#' && char.IsWhiteSpace(value[index - 1]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasOnlyTrailingComment(string value)
    {
        var trailing = value.TrimStart();
        return trailing.Length == 0 || trailing[0] == '#';
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return normalized is { Length: > 0 }
               && normalized.All(character => character is >= '!' and <= '~')
            ? normalized
            : null;
    }
}
