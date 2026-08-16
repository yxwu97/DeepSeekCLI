using System.Diagnostics;

namespace DeepSeekHarnessDesktop.Services;

public static class CmdCommandLineBuilder
{
    private static readonly string[] DshArguments = ["web"];
    private static readonly string[] NpxArguments = ["-y", "@deepseek-ai/dsh@0.1.0-rc.6", "web"];

    public static ProcessStartInfo Build(
        string scriptPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
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

        if (!arguments.SequenceEqual(DshArguments, StringComparer.Ordinal)
            && !arguments.SequenceEqual(NpxArguments, StringComparer.Ordinal))
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
}
