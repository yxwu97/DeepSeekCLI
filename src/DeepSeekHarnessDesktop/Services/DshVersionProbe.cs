using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using NuGet.Versioning;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshVersionProbe : IDshVersionProbe
{
    public const int MaximumOutputCharacters = 4 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<DshVersionProbeResult> ProbeAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo;
        try
        {
            startInfo = CreateStartInfo(executablePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Failure($"Version probe command is invalid: {exception.GetType().Name}.");
        }

        using var job = new WindowsJobObject();
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return Failure("Version probe process did not start.");
            }
            job.Assign(process);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
            await StopProcessAsync(process, job);
            return Failure($"Version probe process failed to start: {exception.GetType().Name}.");
        }

        var outputTask = ReadBoundedAsync(process.StandardOutput);
        var errorTask = ReadBoundedAsync(process.StandardError);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process, job);
            await ObserveReadersAsync(outputTask, errorTask);
            cancellationToken.ThrowIfCancellationRequested();
            return Failure("Version probe timed out.");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (output.ExceededLimit || error.ExceededLimit)
        {
            return Failure("Version probe output exceeded the limit.");
        }
        if (process.ExitCode != 0)
        {
            return Failure($"Version probe exited with code {process.ExitCode}.");
        }

        var value = output.Text.Trim();
        if (value.Length == 0
            || value.IndexOfAny(['\r', '\n']) >= 0
            || !NuGetVersion.TryParse(value, out var version))
        {
            return Failure("Version probe output is not a single semantic version.");
        }

        return new DshVersionProbeResult(true, version.ToNormalizedString());
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath)
    {
        if (string.Equals(Path.GetExtension(executablePath), ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return CmdCommandLineBuilder.BuildVersionProbe(executablePath);
        }

        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new ArgumentException("The version probe executable does not exist.", nameof(executablePath));
        }

        var startInfo = new ProcessStartInfo(fullPath)
        {
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        startInfo.AddArgument("--version");
        return startInfo;
    }

    private static async Task<BoundedText> ReadBoundedAsync(StreamReader reader)
    {
        var builder = new StringBuilder(MaximumOutputCharacters);
        var buffer = new char[1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return new BoundedText(builder.ToString(), false);
            }

            var remaining = MaximumOutputCharacters - builder.Length;
            if (read > remaining)
            {
                return new BoundedText(builder.ToString(), true);
            }
            builder.Append(buffer, 0, read);
        }
    }

    private static async Task StopProcessAsync(Process process, WindowsJobObject job)
    {
        job.Dispose();
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static async Task ObserveReadersAsync(params Task<BoundedText>[] readers)
    {
        foreach (var reader in readers)
        {
            try { await reader; }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }
    }

    private static DshVersionProbeResult Failure(string detail) => new(false, null, detail);

    private sealed record BoundedText(string Text, bool ExceededLimit);
}
