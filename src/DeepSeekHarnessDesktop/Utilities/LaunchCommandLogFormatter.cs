using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Utilities;

public static class LaunchCommandLogFormatter
{
    public static string Format(DshLaunchOptions options)
    {
        var executableName = Path.GetFileName(options.ExecutablePath);
        return string.Equals(Path.GetExtension(executableName), ".cmd", StringComparison.OrdinalIgnoreCase)
            ? $"{executableName} {string.Join(" ", options.Arguments)}"
            : $"{executableName} <arguments omitted>";
    }
}
