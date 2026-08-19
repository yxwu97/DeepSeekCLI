using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Globalization;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCommandResolver : IDshCommandResolver
{
    private readonly IDshCandidateDiscoveryService _discovery;

    public DshCommandResolver(
        Func<string, string?>? getEnvironmentVariable = null,
        NpxDshCacheLocator? cacheLocator = null,
        IDshCandidateDiscoveryService? discovery = null)
    {
        var pathProvider = getEnvironmentVariable is null
            ? new EnvironmentPathProvider()
            : new EnvironmentPathProvider((name, _) => getEnvironmentVariable(name));
        _discovery = discovery ?? new DshCandidateDiscoveryService(
            pathProvider,
            cacheLocator: cacheLocator);
    }

    public async Task<DshLaunchOptions> ResolveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorkspace(settings.WorkspacePath);

        if (settings.Launch.Mode == LaunchMode.Custom)
        {
            return CreateOptions(
                ResolveCustom(settings.Launch.ExecutablePath),
                settings.Launch.Arguments,
                settings);
        }

        var discovery = await _discovery.DiscoverAsync(cancellationToken);
        var candidate = discovery.Candidate;
        if (candidate is null)
        {
            var message = discovery.CanPrepare
                ? "No reusable DSH candidate was found; locked preparation is required."
                : "No reusable DSH candidate was found and Node.js/npm are unavailable.";
            throw Error("DSH-E101", "尚未安装可用的 DSH", message, true);
        }

        var arguments = candidate.Source == DshInstallationSource.GlobalPath
            ? BuildDshArguments(settings.ServiceUri)
            : BuildCachedDshArguments(
                candidate.EntryPointPath
                    ?? throw new InvalidOperationException("A Node-based DSH candidate requires an entry point."),
                settings.ServiceUri);
        return CreateOptions(candidate.ExecutablePath, arguments, settings);
    }

    private static DshLaunchOptions CreateOptions(
        string executable,
        IReadOnlyList<string> arguments,
        AppSettings settings) => new()
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

    private static IReadOnlyList<string> BuildDshArguments(Uri serviceUri)
    {
        var arguments = new List<string> { "web" };
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
        if (!PathCompatibility.IsFullyQualified(workspace) || !Directory.Exists(workspace))
        {
            throw Error("DSH-E102", "工作目录不存在或不可访问", $"Invalid workspace: {workspace}", false);
        }
    }

    private static HarnessException Error(string code, string userMessage, string technicalMessage, bool retryable) =>
        new(new HarnessError(code, userMessage, technicalMessage, retryable));
}
