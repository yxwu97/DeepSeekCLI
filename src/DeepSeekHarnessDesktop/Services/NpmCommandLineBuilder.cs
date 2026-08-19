using System.Diagnostics;

namespace DeepSeekHarnessDesktop.Services;

public static class NpmCommandLineBuilder
{
    private static readonly string[] LockedInstallArguments = ["ci", "--omit=dev"];

    public static ProcessStartInfo BuildLockedInstall(
        string npmPath,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment) =>
        Build(npmPath, LockedInstallArguments, workingDirectory, environment);

    public static ProcessStartInfo Build(
        string npmPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var fullPath = CmdCommandLineBuilder.ValidateScriptPath(npmPath);
        if (!string.Equals(Path.GetFileName(fullPath), "npm.cmd", StringComparison.OrdinalIgnoreCase)
            || !arguments.SequenceEqual(LockedInstallArguments, StringComparer.Ordinal))
        {
            throw new ArgumentException("Only the built-in npm ci --omit=dev command is allowed.");
        }

        return CmdCommandLineBuilder.BuildControlled(
            fullPath,
            LockedInstallArguments,
            workingDirectory,
            environment);
    }
}
