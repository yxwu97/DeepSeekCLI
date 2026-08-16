using DeepSeekHarnessDesktop.Models;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DependencyDiagnosticsService
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
        string? webViewVersion = null;
        var errors = new List<HarnessError>();
        try
        {
            webViewVersion = _getWebView2Version();
            if (string.IsNullOrWhiteSpace(webViewVersion))
            {
                throw new InvalidOperationException("WebView2 Runtime version is empty.");
            }
        }
        catch (Exception exception)
        {
            errors.Add(new HarnessError(
                "WEB-E301",
                "WebView2 Runtime 不可用",
                exception.Message,
                false,
                exception));
        }

        var path = _getEnvironmentVariable("PATH");
        var nodePath = FindOnPath("node.exe", path);
        var npxPath = FindOnPath("npx.cmd", path);
        string? nodeVersion = null;
        if (nodePath is not null)
        {
            try
            {
                nodeVersion = await _getExecutableVersion(nodePath, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add(new HarnessError(
                    "DSH-E101",
                    "Node.js 无法运行，请检查安装或 PATH",
                    exception.Message,
                    false,
                    exception));
            }
        }

        if (nodePath is null || npxPath is null)
        {
            errors.Add(new HarnessError(
                "DSH-E101",
                "未找到 Node.js 或 npx，请检查安装和 PATH",
                $"node.exe: {nodePath ?? "missing"}; npx.cmd: {npxPath ?? "missing"}",
                false));
        }

        return new DependencyDiagnosticsResult(
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown",
            Environment.Version.ToString(),
            webViewVersion,
            nodeVersion,
            npxPath,
            "0.1.0-rc.6",
            errors);
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
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--version");
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start Node.js version check.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Node.js version check exited with {process.ExitCode}: {error}");
        }
        return output;
    }
}
