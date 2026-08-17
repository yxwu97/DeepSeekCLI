using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Globalization;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCommandResolver : IDshCommandResolver
{
    private readonly EnvironmentPathProvider _pathProvider;

    public DshCommandResolver(Func<string, string?>? getEnvironmentVariable = null)
    {
        _pathProvider = getEnvironmentVariable is null
            ? new EnvironmentPathProvider()
            : new EnvironmentPathProvider((name, _) => getEnvironmentVariable(name));
    }

    public Task<DshLaunchOptions> ResolveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateWorkspace(settings.WorkspacePath);

        var executable = settings.Launch.Mode == LaunchMode.Custom
            ? ResolveCustom(settings.Launch.ExecutablePath)
            : FindOnPath("dsh.cmd") ?? FindOnPath("npx.cmd");
        if (executable is null)
        {
            throw Error("DSH-E101", "未找到 dsh 或 npx，请检查 Node.js 安装", "Neither dsh.cmd nor npx.cmd was found on PATH.", false);
        }

        IReadOnlyList<string> arguments = settings.Launch.Mode == LaunchMode.Custom
            ? settings.Launch.Arguments
            : string.Equals(Path.GetFileName(executable), "dsh.cmd", StringComparison.OrdinalIgnoreCase)
                ? BuildDshArguments(settings.ServiceUri)
                : BuildNpxArguments(settings.ServiceUri);

        return Task.FromResult(new DshLaunchOptions
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
        });
    }

    private static IReadOnlyList<string> BuildDshArguments(Uri serviceUri)
    {
        var arguments = new List<string> { "web" };
        AppendPortIfNeeded(arguments, serviceUri);
        return arguments;
    }

    private static IReadOnlyList<string> BuildNpxArguments(Uri serviceUri)
    {
        var arguments = new List<string>
        {
            "-y",
            DshPackageMetadata.PackageName,
            "web",
        };
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

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.GetFullPath(Path.Combine(entry.Trim('"'), fileName));
                if (File.Exists(candidate) && (File.GetAttributes(candidate) & FileAttributes.Directory) == 0)
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Ignore malformed or inaccessible PATH entries and continue searching.
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
