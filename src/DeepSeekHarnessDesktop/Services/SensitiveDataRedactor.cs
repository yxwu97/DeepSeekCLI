using System.Collections;
using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Services;

public sealed partial class SensitiveDataRedactor
{
    private const string Replacement = "[REDACTED]";
    private readonly IReadOnlyList<string> _sensitiveValues;

    public SensitiveDataRedactor(IDictionary? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariables();
        _sensitiveValues = environment.Keys
            .Cast<object>()
            .Select(key => key.ToString())
            .Where(IsSensitiveVariableName)
            .Select(key => environment[key!]?.ToString())
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value!.Length)
            .Cast<string>()
            .ToArray();
    }

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var redacted = BearerTokenRegex().Replace(text, match => $"{match.Groups[1].Value}{Replacement}");
        redacted = SensitiveQueryRegex().Replace(redacted, match => $"{match.Groups[1].Value}{Replacement}");
        foreach (var value in _sensitiveValues)
        {
            redacted = redacted.Replace(value, Replacement, StringComparison.Ordinal);
        }
        return redacted;
    }

    private static bool IsSensitiveVariableName(string? name) =>
        name?.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) == true
        || name?.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) == true
        || name?.Contains("SECRET", StringComparison.OrdinalIgnoreCase) == true;

    [GeneratedRegex(@"(?i)(Authorization\s*:\s*Bearer\s+)[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)([?&](?:key|token|secret)=)[^&#\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveQueryRegex();
}
