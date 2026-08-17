using System.Diagnostics;
using System.Globalization;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.Services;

public static class CmdCommandLineBuilder
{
    private static readonly string[] DshArguments = ["web"];
    private static readonly string[] NpxArguments = ["-y", DshPackageMetadata.PackageName, "web"];

    public static ProcessStartInfo Build(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var fullPath = ValidateScriptPath(scriptPath);

        if (!IsAllowedArguments(arguments))
        {
            throw new ArgumentException("Only the built-in DSH command arguments are accepted for .cmd scripts.", nameof(arguments));
        }

        var command = $"\"\"{fullPath}\" {string.Join(' ', arguments)}\"";
        var startInfo = CreateBaseStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            workingDirectory,
            environment);
        startInfo.Arguments = $"/d /v:off /s /c {command}";
        return startInfo;
    }

    internal static ProcessStartInfo BuildVersionProbe(string scriptPath)
    {
        var fullPath = ValidateScriptPath(scriptPath);
        var startInfo = CreateBaseStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Path.GetDirectoryName(fullPath)!,
            new Dictionary<string, string>());
        startInfo.Arguments = $"/d /v:off /s /c \"\"{fullPath}\" --version\"";
        return startInfo;
    }

    public static ProcessStartInfo BuildNative(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = CreateBaseStartInfo(executablePath, workingDirectory, environment);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateBaseStartInfo(
        string executablePath,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    private static string ValidateScriptPath(string scriptPath)
    {
        var fullPath = Path.GetFullPath(scriptPath);
        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The command script must be an existing .cmd file.", nameof(scriptPath));
        }

        if (fullPath.IndexOfAny(['%', '\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException("The command script path contains an unsafe character.", nameof(scriptPath));
        }

        return fullPath;
    }

    private static bool IsAllowedArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.SequenceEqual(DshArguments, StringComparer.Ordinal)
            || arguments.SequenceEqual(NpxArguments, StringComparer.Ordinal))
        {
            return true;
        }

        var prefixLength = arguments.Count - 2;
        if (prefixLength < 1
            || arguments[prefixLength] != "--port"
            || !int.TryParse(arguments[prefixLength + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        var prefix = arguments.Take(prefixLength);
        return prefix.SequenceEqual(DshArguments, StringComparer.Ordinal)
            || prefix.SequenceEqual(NpxArguments, StringComparer.Ordinal);
    }
}
