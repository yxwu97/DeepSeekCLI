using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DependencyDiagnosticsService : IDependencyDiagnosticsService
{
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string?> _getWebView2Version;
    private readonly Func<string, CancellationToken, Task<string?>> _getExecutableVersion;

    public DependencyDiagnosticsService(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string?>? getWebView2Version = null,
        Func<string, CancellationToken, Task<string?>>? getExecutableVersion = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _getWebView2Version = getWebView2Version ?? (() => CoreWebView2Environment.GetAvailableBrowserVersionString());
        _getExecutableVersion = getExecutableVersion ?? GetExecutableVersionAsync;
    }

    public async Task<DependencyDiagnosticsResult> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var errors = new List<HarnessError>();
        var webView = DiagnoseWebView(errors);
        var path = _getEnvironmentVariable("PATH");
        var dshPath = FindOnPath("dsh.cmd", path);
        var nodePath = FindOnPath("node.exe", path);
        var npxPath = FindOnPath("npx.cmd", path);
        var dsh = await DiagnoseGlobalDshAsync(dshPath, cancellationToken);
        var node = await DiagnoseNodeAsync(nodePath, cancellationToken);
        var npx = npxPath is null
            ? new DependencyCheck(DependencyStatus.Missing, Detail: "npx.cmd was not found on PATH.")
            : new DependencyCheck(DependencyStatus.Available, npxPath);

        if (dsh.Status != DependencyStatus.Available
            && (node.Status != DependencyStatus.Available || npx.Status != DependencyStatus.Available))
        {
            errors.Add(new HarnessError(
                "DSH-E101",
                "未找到可用的 DSH，且 Node.js 或 npx 不可用",
                $"dsh: {dsh.Status}; node: {node.Status}; npx: {npx.Status}",
                true));
        }

        return new DependencyDiagnosticsResult(
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown",
            Environment.Version.ToString(),
            webView,
            dsh,
            node,
            npx,
            errors);
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

    private async Task<DependencyCheck> DiagnoseGlobalDshAsync(string? path, CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return new DependencyCheck(DependencyStatus.Missing, Detail: "dsh.cmd was not found on PATH.");
        }

        try
        {
            var version = await _getExecutableVersion(path, cancellationToken);
            return new DependencyCheck(DependencyStatus.Available, path, version);
        }
        catch (ArgumentException exception)
        {
            return new DependencyCheck(DependencyStatus.Unusable, path, Detail: exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DependencyCheck(DependencyStatus.Available, path, Detail: exception.Message);
        }
    }

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

    internal static string? FindOnPath(string fileName, string? path)
    {
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
            }
        }

        return null;
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
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
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
                process.Kill(entireProcessTree: true);
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
        startInfo.ArgumentList.Add("--version");
        return startInfo;
    }
}
