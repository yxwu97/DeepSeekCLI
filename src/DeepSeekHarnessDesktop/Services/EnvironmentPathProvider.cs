namespace DeepSeekHarnessDesktop.Services;

public sealed class EnvironmentPathProvider
{
    private readonly Func<string, EnvironmentVariableTarget, string?> _getEnvironmentVariable;

    public EnvironmentPathProvider(
        Func<string, EnvironmentVariableTarget, string?>? getEnvironmentVariable = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public string? GetSearchPath()
    {
        var entries = GetEntries(EnvironmentVariableTarget.Machine)
            .Concat(GetEntries(EnvironmentVariableTarget.User))
            .Concat(GetEntries(EnvironmentVariableTarget.Process))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var result = string.Join(Path.PathSeparator, entries);
        return result.Length == 0 ? null : result;
    }

    private IEnumerable<string> GetEntries(EnvironmentVariableTarget target)
    {
        var path = _getEnvironmentVariable("PATH", target);
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var entry in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = entry.Trim('"');
            if (normalized.Length != 0)
            {
                yield return normalized;
            }
        }
    }
}
