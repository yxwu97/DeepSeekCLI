using System.Diagnostics;

namespace DeepSeekHarnessDesktop.Services;

public static class NpmCommandLineBuilder
{
    public static IReadOnlyList<string> CreateLockedInstallArgumentsForWorkingDirectory(
        string workingDirectory)
    {
        var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        var stagingRoot = Directory.GetParent(fullWorkingDirectory)?.FullName
            ?? throw new ArgumentException("The npm working directory has no parent.");
        var privateRoot = Directory.GetParent(stagingRoot)?.FullName
            ?? throw new ArgumentException("The npm staging root has no parent.");
        var configurationRoot = Path.Combine(
            privateRoot,
            "npm-config",
            Path.GetFileName(fullWorkingDirectory));
        return CreateLockedInstallArguments(
            Path.Combine(privateRoot, "npm-cache"),
            Path.Combine(configurationRoot, "user.npmrc"),
            Path.Combine(configurationRoot, "global.npmrc"));
    }

    public static IReadOnlyList<string> CreateLockedInstallArguments(
        string cacheRoot,
        string userConfig,
        string globalConfig) =>
    [
        "ci",
        "--omit=dev",
        "--ignore-scripts",
        "--registry=https://registry.npmjs.org",
        "--offline=false",
        "--prefer-offline=false",
        "--audit=false",
        "--fund=false",
        "--replace-registry-host=always",
        $"--cache={Path.GetFullPath(cacheRoot)}",
        $"--userconfig={Path.GetFullPath(userConfig)}",
        $"--globalconfig={Path.GetFullPath(globalConfig)}",
    ];

    public static ProcessStartInfo BuildLockedInstall(
        string npmPath,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        return Build(
            npmPath,
            CreateLockedInstallArgumentsForWorkingDirectory(workingDirectory),
            workingDirectory,
            environment);
    }

    public static ProcessStartInfo Build(
        string npmPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var fullPath = CmdCommandLineBuilder.ValidateScriptPath(npmPath);
        var expectedArguments = CreateLockedInstallArgumentsForWorkingDirectory(workingDirectory);
        if (!string.Equals(Path.GetFileName(fullPath), "npm.cmd", StringComparison.OrdinalIgnoreCase)
            || !arguments.SequenceEqual(expectedArguments, StringComparer.Ordinal))
        {
            throw new ArgumentException("Only the built-in npm ci --omit=dev command is allowed.");
        }

        return CmdCommandLineBuilder.BuildControlled(
            fullPath,
            arguments,
            workingDirectory,
            environment);
    }
}
