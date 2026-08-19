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
        var result = string.Join(Path.PathSeparator.ToString(), entries);
        return result.Length == 0 ? null : result;
    }

    public string? FindOnPath(string fileName)
    {
        var path = GetSearchPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var entry in path!.Split(
                     new[] { Path.PathSeparator },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(entry.Trim().Trim('"'), fileName));
                if (File.Exists(candidate)
                    && (File.GetAttributes(candidate) & FileAttributes.Directory) == 0)
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private IEnumerable<string> GetEntries(EnvironmentVariableTarget target)
    {
        var path = _getEnvironmentVariable("PATH", target);
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var entry in path!.Split(
                     new[] { Path.PathSeparator },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = entry.Trim().Trim('"');
            if (normalized.Length != 0)
            {
                yield return normalized;
            }
        }
    }
}
