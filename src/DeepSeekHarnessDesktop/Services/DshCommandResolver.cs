using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Globalization;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCommandResolver : IDshCommandResolver
{
    private readonly EnvironmentPathProvider _pathProvider;
    private readonly NpxDshCacheLocator _cacheLocator;

    public DshCommandResolver(
        Func<string, string?>? getEnvironmentVariable = null,
        NpxDshCacheLocator? cacheLocator = null)
    {
        _pathProvider = getEnvironmentVariable is null
            ? new EnvironmentPathProvider()
            : new EnvironmentPathProvider((name, _) => getEnvironmentVariable(name));
        _cacheLocator = cacheLocator ?? new NpxDshCacheLocator();
    }

    public async Task<DshLaunchOptions> ResolveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorkspace(settings.WorkspacePath);

        var customExecutable = settings.Launch.Mode == LaunchMode.Custom
            ? ResolveCustom(settings.Launch.ExecutablePath)
            : null;
        var globalDsh = settings.Launch.Mode == LaunchMode.Auto ? FindOnPath("dsh.cmd") : null;
        var node = globalDsh is null && settings.Launch.Mode == LaunchMode.Auto ? FindOnPath("node.exe") : null;
        var cachedDsh = globalDsh is null
            ? await _cacheLocator.FindAsync(node, cancellationToken)
            : null;
        var npx = globalDsh is null && cachedDsh is null && settings.Launch.Mode == LaunchMode.Auto
            ? FindOnPath("npx.cmd")
            : null;
        var executable = customExecutable ?? globalDsh ?? cachedDsh?.NodePath ?? npx;
        if (executable is null)
        {
            throw Error(
                "DSH-E101",
                "未找到 dsh 或 npx，请先安装 Node.js",
                "Neither dsh.cmd nor npx.cmd was found on PATH.",
                false);
        }

        IReadOnlyList<string> arguments = customExecutable is not null
            ? settings.Launch.Arguments
            : globalDsh is not null
                ? BuildDshArguments(settings.ServiceUri)
                : cachedDsh is not null
                    ? BuildCachedDshArguments(cachedDsh.EntryPointPath, settings.ServiceUri)
                    : BuildNpxArguments(settings.ServiceUri);

        return new DshLaunchOptions
        {
            ExecutablePath = executable,
            Arguments = arguments,
            WorkingDirectory = Path.GetFullPath(settings.WorkspacePath),
            FallbackUri = settings.ServiceUri,
            StartupTimeout = TimeSpan.FromSeconds(settings.StartupTimeoutSeconds),
            Environment = new Dictionary<string, string>
            {
                ["DSH_DESKTOP_HOST"] = "1",
                ["DSH_DESKTOP_VERSION"] = GetDesktopVersion(),
            },
        };
    }

    private static IReadOnlyList<string> BuildDshArguments(Uri serviceUri)
    {
        var arguments = new List<string> { "web" };
        AppendPortIfNeeded(arguments, serviceUri);
        return arguments;
    }

    private static IReadOnlyList<string> BuildNpxArguments(Uri serviceUri)
    {
        var arguments = new List<string> { "-y", DshPackageMetadata.ValidatedPackageSpec, "web" };
        AppendPortIfNeeded(arguments, serviceUri);
        return arguments;
    }

    private static IReadOnlyList<string> BuildCachedDshArguments(string entryPointPath, Uri serviceUri)
    {
        var arguments = new List<string> { entryPointPath, "web" };
        AppendPortIfNeeded(arguments, serviceUri);
        return arguments;
    }

    private static void AppendPortIfNeeded(List<string> arguments, Uri serviceUri)
    {
        var normalized = ServiceUriValidator.NormalizeOrThrow(serviceUri);
        if (normalized.Port != DshPackageMetadata.DefaultPort)
        {
            arguments.Add("--port");
            arguments.Add(normalized.Port.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string GetDesktopVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";

    private string? FindOnPath(string fileName)
    {
        var path = _pathProvider.GetSearchPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var entry in path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(entry.Trim('"'), fileName));
                if (File.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.Directory) == 0)
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

    private static string ResolveCustom(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw Error("DSH-E101", "自定义 DSH 程序无效", "Custom executable path is empty.", false);
        }

        var fullPath = Path.GetFullPath(path);
        var extension = Path.GetExtension(fullPath);
        if (!File.Exists(fullPath)
            || (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase)))
        {
            throw Error("DSH-E101", "自定义 DSH 程序无效", "Custom mode accepts an existing .exe or .com file only.", false);
        }

        return fullPath;
    }

    private static void ValidateWorkspace(string workspace)
    {
        if (!Path.IsPathFullyQualified(workspace) || !Directory.Exists(workspace))
        {
            throw Error("DSH-E102", "工作目录不存在或不可访问", $"Invalid workspace: {workspace}", false);
        }
    }

    private static HarnessException Error(string code, string userMessage, string technicalMessage, bool retryable) =>
        new(new HarnessError(code, userMessage, technicalMessage, retryable));
}
