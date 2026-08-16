using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace DeepSeekHarnessDesktop.Phase0Validation;

internal static class Phase0Runner
{
    public static async Task<int> RunSelfTestsAsync()
    {
        var failures = new List<string>();
        await RunCheckAsync("cmd special-path execution", ValidateCmdPathAsync, failures);
        await RunCheckAsync("Job Object descendant cleanup", ValidateJobCleanupAsync, failures);
        await RunCheckAsync("DSH identity probe", ValidateDshIdentityAsync, failures);

        foreach (var failure in failures)
        {
            await Console.Error.WriteLineAsync($"FAIL: {failure}");
        }

        if (failures.Count == 0)
        {
            await Console.Out.WriteLineAsync("PASS: all non-interactive Phase 0 checks completed.");
        }

        return failures.Count == 0 ? 0 : 1;
    }

    public static async Task<int> RunJobParentAsync()
    {
        await Task.Delay(750);
        using var child = Process.Start(CreateSelfStartInfo("--job-child", redirectOutput: false))
            ?? throw new InvalidOperationException("Cannot start validation child process.");
        await Console.Out.WriteLineAsync(child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Console.Out.FlushAsync();
        await child.WaitForExitAsync();
        return 0;
    }

    private static async Task RunCheckAsync(
        string name,
        Func<Task> check,
        ICollection<string> failures)
    {
        try
        {
            await check();
            await Console.Out.WriteLineAsync($"PASS: {name}");
        }
        catch (Exception exception)
        {
            failures.Add($"{name}: {exception.Message}");
        }
    }

    private static async Task ValidateCmdPathAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "DSH Phase0 & (Unicode 中文)",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "probe & (ok) 中文.cmd");
        await File.WriteAllTextAsync(scriptPath, "@echo PHASE0_CMD_OK\r\n");

        try
        {
            var command = $"\"\"{scriptPath}\"\"";
            using var process = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = $"/d /v:off /s /c {command}",
            }) ?? throw new InvalidOperationException("cmd.exe did not start.");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !output.Contains("PHASE0_CMD_OK", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Exit={process.ExitCode}; stderr={error.Trim()}");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ValidateJobCleanupAsync()
    {
        using var parent = Process.Start(CreateSelfStartInfo("--job-parent", redirectOutput: true))
            ?? throw new InvalidOperationException("Cannot start validation parent process.");

        using (var job = new WindowsJobObject())
        {
            job.Assign(parent);
            var line = await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (!int.TryParse(line, out var childId))
            {
                throw new InvalidOperationException("Child PID was not reported.");
            }

            using var child = Process.GetProcessById(childId);
            job.Dispose();
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static ProcessStartInfo CreateSelfStartInfo(string mode, bool redirectOutput)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the current process host.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }

        startInfo.ArgumentList.Add(mode);
        return startInfo;
    }

    private static async Task ValidateDshIdentityAsync()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync("http://127.0.0.1:3080/");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                "No service is listening on 127.0.0.1:3080; start locked DSH before this check.");
        }

        using (response)
        {
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                throw new InvalidOperationException($"Unexpected HTTP status {(int)response.StatusCode}.");
            }

            var html = await response.Content.ReadAsStringAsync();
            if (!html.Contains("<title>DeepSeek Harness</title>", StringComparison.Ordinal)
                || !html.Contains("window.__DSH_BOOT__", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The DSH title/boot-marker pair was not found.");
            }
        }
    }
}
