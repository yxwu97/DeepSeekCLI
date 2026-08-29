namespace DeepSeekHarnessDesktop.Services;

public static class ProcessEnvironmentPolicy
{
    public static IReadOnlyDictionary<string, string> Apply(
        IReadOnlyDictionary<string, string> inherited,
        IReadOnlyDictionary<string, string> overrides,
        IReadOnlyCollection<string> removals,
        IReadOnlyCollection<string> removalPrefixes)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in inherited)
        {
            values[pair.Key] = pair.Value;
        }
        foreach (var name in removals)
        {
            ValidateName(name, "removal name");
            values.Remove(name);
        }
        foreach (var prefix in removalPrefixes)
        {
            ValidateName(prefix, "removal prefix");
            foreach (var name in values.Keys
                .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToArray())
            {
                values.Remove(name);
            }
        }
        foreach (var pair in overrides)
        {
            ValidateName(pair.Key, "name");
            if (pair.Value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Process environment contains an invalid value.");
            }
            values[pair.Key] = pair.Value;
        }
        return values;
    }

    private static void ValidateName(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.IndexOf('\0') >= 0)
        {
            throw new ArgumentException($"Process environment contains an invalid {description}.");
        }
    }
}
