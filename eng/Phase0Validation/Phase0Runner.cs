using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Net.Sockets;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;

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

    public static async Task<int> RunPrivateDshSmokeAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DeepSeekHarnessDesktop",
            "private-smoke",
            Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        var privateRoot = Path.Combine(root, "private");
        var npmCache = Path.Combine(root, "npm-cache");
        var dshHome = Path.Combine(root, "dsh-home");
        Directory.CreateDirectory(workspace);
        var environment = SetTemporaryEnvironment(new Dictionary<string, string>
        {
            ["npm_config_cache"] = npmCache,
            ["npm_config_registry"] = "https://registry.npmjs.org/",
            ["npm_config_audit"] = "false",
            ["npm_config_fund"] = "false",
            ["DSH_HOME"] = dshHome,
        });

        try
        {
            var pathProvider = new EnvironmentPathProvider();
            var nodePath = pathProvider.FindOnPath("node.exe")
                ?? throw new InvalidOperationException("node.exe was not found on PATH.");
            var npmPath = pathProvider.FindOnPath("npm.cmd")
                ?? throw new InvalidOperationException("npm.cmd was not found on PATH.");
            var store = new PrivateDshInstallationStore(
                () => privateRoot,
                () => Path.Combine(AppContext.BaseDirectory, "dsh-runtime"));
            var discovery = new PrivateOnlyDiscovery(pathProvider, store);
            var logs = new RecentLogBuffer();
            logs.LineAdded += (_, line) => Console.Out.WriteLine(line.DisplayText);
            var runner = new CountingNpmInstallRunner(new NpmInstallRunner(logs));
            await using var processManager = new HarnessProcessManager(logs);
            using var healthMonitor = new HarnessHealthMonitor();
            var preparation = new DshPreparationService(
                discovery,
                store,
                runner,
                processManager,
                healthMonitor,
                logs);
            var port = ReserveLoopbackPort();
            var settings = new AppSettings
            {
                WorkspacePath = workspace,
                ServiceUri = new Uri($"http://127.0.0.1:{port}/"),
                Launch = new LaunchSettings { Mode = LaunchMode.Auto },
            };

            await preparation.PrepareAsync(settings, CancellationToken.None);
            await preparation.PrepareAsync(settings, CancellationToken.None);
            if (runner.RunCount != 1)
            {
                throw new InvalidOperationException($"Expected one npm install, observed {runner.RunCount}.");
            }

            var resolver = new DshCommandResolver(discovery: discovery);
            var options = await resolver.ResolveAsync(settings, CancellationToken.None);
            var process = await processManager.StartAsync(options, CancellationToken.None);
            var ready = await healthMonitor.WaitUntilReadyAsync(
                () => settings.ServiceUri,
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
            if (ready.Status != HealthProbeStatus.DshConfirmed)
            {
                throw new InvalidOperationException($"Private DSH identity failed: {ready.Status} {ready.Detail}");
            }
            await processManager.StopAsync(CancellationToken.None);

            var bytes = GetDirectorySize(privateRoot);
            Console.Out.WriteLine(
                $"PASS: private DSH installed once, reused without npm, PID {process.ProcessId}, "
                + $"private bytes {bytes}.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: private DSH smoke: {exception}");
            return 1;
        }
        finally
        {
            RestoreEnvironment(environment);
            await DeleteValidationRootAsync(root);
        }
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
        File.WriteAllText(scriptPath, "@echo PHASE0_CMD_OK\r\n");

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
        var processPath = Assembly.GetExecutingAssembly().Location;
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.AddArgument(Assembly.GetExecutingAssembly().Location);
        }

        startInfo.AddArgument(mode);
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

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static Dictionary<string, string?> SetTemporaryEnvironment(
        IReadOnlyDictionary<string, string> values)
    {
        var original = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            original[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
        return original;
    }

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var pair in values)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private static async Task DeleteValidationRootAsync(string root)
    {
        var expectedParent = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "DeepSeekHarnessDesktop",
            "private-smoke"));
        if (!string.Equals(
            Path.GetFullPath(Path.GetDirectoryName(root)!),
            expectedParent,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Exception? lastError = null;
        foreach (var delay in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1) })
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }
            try
            {
                if (!Directory.Exists(root))
                {
                    return;
                }
                ClearReadOnlyFiles(root);
                Directory.Delete(root, true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
            }
        }
        Console.Error.WriteLine(
            $"WARN: validation root cleanup did not complete: {lastError?.GetType().Name ?? "unknown"}.");
    }

    private static long GetDirectorySize(string root)
    {
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(path).Length; }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        return total;
    }

    private static void ClearReadOnlyFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class PrivateOnlyDiscovery(
        EnvironmentPathProvider pathProvider,
        IPrivateDshInstallationStore store) : IDshCandidateDiscoveryService
    {
        public async Task<DshDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
        {
            var node = pathProvider.FindOnPath("node.exe");
            var candidate = await store.FindActiveAsync(node, cancellationToken);
            return new DshDiscoveryResult(
                candidate,
                node,
                pathProvider.FindOnPath("npm.cmd"),
                pathProvider.FindOnPath("npx.cmd"));
        }
    }

    private sealed class CountingNpmInstallRunner(INpmInstallRunner inner) : INpmInstallRunner
    {
        public int RunCount { get; private set; }

        public Task RunAsync(
            string npmPath,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            RunCount++;
            return inner.RunAsync(npmPath, workingDirectory, cancellationToken);
        }
    }
}
