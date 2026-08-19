using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DependencyDiagnosticsService : IDependencyDiagnosticsService
{
    private readonly Func<string?> _getWebView2Version;
    private readonly Func<string, CancellationToken, Task<string?>> _getExecutableVersion;
    private readonly IDshCandidateDiscoveryService _discovery;

    public DependencyDiagnosticsService(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string?>? getWebView2Version = null,
        Func<string, CancellationToken, Task<string?>>? getExecutableVersion = null,
        NpxDshCacheLocator? cacheLocator = null,
        IDshCandidateDiscoveryService? discovery = null)
    {
        var pathProvider = getEnvironmentVariable is null
            ? new EnvironmentPathProvider()
            : new EnvironmentPathProvider((name, _) => getEnvironmentVariable(name));
        _getWebView2Version = getWebView2Version ?? (() => CoreWebView2Environment.GetAvailableBrowserVersionString());
        _getExecutableVersion = getExecutableVersion ?? GetExecutableVersionAsync;
        _discovery = discovery ?? new DshCandidateDiscoveryService(
            pathProvider,
            cacheLocator: cacheLocator);
    }

    public async Task<DependencyDiagnosticsResult> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var errors = new List<HarnessError>();
        var webView = DiagnoseWebView(errors);
        var discovery = await _discovery.DiscoverAsync(cancellationToken);
        var dsh = await DiagnoseDshAsync(discovery.Candidate, cancellationToken);
        var node = await DiagnoseNodeAsync(discovery.NodePath, cancellationToken);
        var npm = ToolCheck(discovery.NpmPath, "npm.cmd");
        var npx = ToolCheck(discovery.NpxPath, "npx.cmd");

        if (dsh.Status != DependencyStatus.Available
            && (node.Status != DependencyStatus.Available || npm.Status != DependencyStatus.Available))
        {
            errors.Add(new HarnessError(
                "DSH-E101",
                "未找到可用的 DSH，且 Node.js 或 npm 不可用",
                $"dsh: {dsh.Status}; node: {node.Status}; npm: {npm.Status}",
                true));
        }

        return new DependencyDiagnosticsResult(
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown",
            Environment.Version.ToString(),
            webView,
            dsh,
            node,
            npx,
            errors,
            npm,
            discovery.Candidate?.Source ?? DshInstallationSource.None);
    }

    private DependencyCheck DiagnoseWebView(List<HarnessError> errors)
    {
        try
        {
            var version = _getWebView2Version();
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException("WebView2 Runtime version is empty.");
            }

            return new DependencyCheck(DependencyStatus.Available, Version: version);
        }
        catch (Exception exception)
        {
            errors.Add(new HarnessError("WEB-E301", "WebView2 Runtime 不可用", exception.Message, false, exception));
            return new DependencyCheck(DependencyStatus.Unusable, Detail: exception.Message);
        }
    }

    private async Task<DependencyCheck> DiagnoseDshAsync(
        DshInstallationCandidate? candidate,
        CancellationToken cancellationToken)
    {
        if (candidate is null)
        {
            return new DependencyCheck(DependencyStatus.Missing, Detail: "No reusable DSH installation was found.");
        }

        if (candidate.Source != DshInstallationSource.GlobalPath)
        {
            return new DependencyCheck(
                DependencyStatus.Available,
                candidate.EntryPointPath,
                candidate.Version,
                $"Validated {candidate.Source} installation.");
        }

        try
        {
            var version = await _getExecutableVersion(candidate.ExecutablePath, cancellationToken);
            return new DependencyCheck(DependencyStatus.Available, candidate.ExecutablePath, version);
        }
        catch (ArgumentException exception)
        {
            return new DependencyCheck(DependencyStatus.Unusable, candidate.ExecutablePath, Detail: exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DependencyCheck(DependencyStatus.Available, candidate.ExecutablePath, Detail: exception.Message);
        }
    }

    private static DependencyCheck ToolCheck(string? path, string fileName) => path is null
        ? new DependencyCheck(DependencyStatus.Missing, Detail: $"{fileName} was not found on PATH.")
        : new DependencyCheck(DependencyStatus.Available, path);

    private async Task<DependencyCheck> DiagnoseNodeAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return new DependencyCheck(DependencyStatus.Missing, Detail: "node.exe was not found on PATH.");
        }

        try
        {
            var version = await _getExecutableVersion(path, cancellationToken);
            return new DependencyCheck(DependencyStatus.Available, path, version);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DependencyCheck(DependencyStatus.Unusable, path, Detail: exception.Message);
        }
    }

    private static async Task<string?> GetExecutableVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        var startInfo = string.Equals(Path.GetExtension(executablePath), ".cmd", StringComparison.OrdinalIgnoreCase)
            ? CmdCommandLineBuilder.BuildVersionProbe(executablePath)
            : BuildNativeVersionProbe(executablePath);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start dependency version check.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(timeout.Token);
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Version check exited with {process.ExitCode}: {error}");
            }

            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static ProcessStartInfo BuildNativeVersionProbe(string executablePath)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.AddArgument("--version");
        return startInfo;
    }
}
