using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.TestHarness;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.IntegrationTests;

public sealed class HarnessProcessManagerTests
{
    [Fact]
    public async Task CapturesOutputAndStopsOwnedProcess()
    {
        await using var manager = new HarnessProcessManager();
        var output = new TaskCompletionSource<ProcessOutputLine>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OutputReceived += (_, args) =>
        {
            if (args.Line.Text.Contains("43123", StringComparison.Ordinal))
            {
                output.TrySetResult(args.Line);
            }
        };

        var info = await manager.StartAsync(CreateOptions("--emit"), CancellationToken.None);
        var line = await output.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.StopAsync(CancellationToken.None);

        Assert.True(info.ProcessId > 0);
        Assert.Equal("server http://127.0.0.1:43123/", line.Text);
        Assert.False(manager.IsRunning);
        Assert.Null(manager.Current);
    }

    [Fact]
    public async Task JobObjectStopsDescendantProcess()
    {
        await using var manager = new HarnessProcessManager();
        var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OutputReceived += (_, args) =>
        {
            const string prefix = "CHILD_PID=";
            if (args.Line.Text.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(args.Line.Text.Substring(prefix.Length), out var pid))
            {
                childPid.TrySetResult(pid);
            }
        };

        await manager.StartAsync(CreateOptions("--tree"), CancellationToken.None);
        var pid = await childPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.StopAsync(CancellationToken.None);

        await AssertProcessExitedAsync(pid);
    }

    [Fact]
    public async Task ReportsImmediateExitCode()
    {
        await using var manager = new HarnessProcessManager();
        var exited = new TaskCompletionSource<ProcessExitedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ProcessExited += (_, args) => exited.TrySetResult(args);

        var info = await manager.StartAsync(CreateOptions("--exit"), CancellationToken.None);
        var result = await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(info.ProcessId, result.ProcessId);
        Assert.Equal(23, result.ExitCode);
    }

    [Fact]
    public async Task SuspendedLauncherPreservesStructuredArguments()
    {
        await using var manager = new HarnessProcessManager();
        var output = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OutputReceived += (_, args) =>
        {
            if (args.Line.Text.StartsWith("ARGS=", StringComparison.Ordinal))
            {
                output.TrySetResult(args.Line.Text);
            }
        };
        var options = CreateOptions("--echo-args") with
        {
            Arguments =
            [
                "--echo-args",
                "space value",
                "quote\"value",
                "trailing\\",
                "&|<>^%!()中文",
            ],
        };

        await manager.StartAsync(options, CancellationToken.None);
        var result = await output.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("ARGS=space value|quote\"value|trailing\\|&|<>^%!()中文", result);
    }

    [Fact]
    public async Task StalledNpmInstallReturnsE221AndReapsProcessTree()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DSH-IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "npm.cmd");
            File.WriteAllText(script, $"@echo off\r\n\"{typeof(HarnessMarker).Assembly.Location}\" --tree\r\n");
            var logs = new RecentLogBuffer();
            var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            logs.LineAdded += (_, line) =>
            {
                const string prefix = "CHILD_PID=";
                if (line.Text.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(line.Text.Substring(prefix.Length), out var pid))
                {
                    childPid.TrySetResult(pid);
                }
            };
            var runner = new NpmInstallRunner(
                logs,
                preparationTimeout: TimeSpan.FromMilliseconds(100),
                noProgressTimeout: TimeSpan.FromMilliseconds(100));

            var run = Assert.ThrowsAsync<HarnessException>(() => runner.RunAsync(
                script,
                directory,
                CancellationToken.None));
            var pid = await childPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var exception = await run.WaitAsync(TimeSpan.FromSeconds(8));

            Assert.Equal("DSH-E221", exception.Error.Code);
            await AssertProcessExitedAsync(pid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NpmExitWithInheritedOutputPipeFailsWithinDeadlineAndReapsDescendant()
    {
        var fixture = CreateNpmFixture("--spawn-and-exit");
        try
        {
            var runner = new NpmInstallRunner(
                fixture.Logs,
                outputDrainTimeout: TimeSpan.FromMilliseconds(200));
            var run = Assert.ThrowsAsync<HarnessException>(() => runner.RunAsync(
                fixture.ScriptPath,
                fixture.Root,
                CancellationToken.None));
            var pid = await fixture.ChildPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var exception = await run.WaitAsync(TimeSpan.FromSeconds(8));

            Assert.Equal("DSH-E201", exception.Error.Code);
            await AssertProcessExitedAsync(pid);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task CancellingNpmInstallReapsProcessTree()
    {
        var fixture = CreateNpmFixture("--tree");
        try
        {
            using var cancellation = new CancellationTokenSource();
            var runner = new NpmInstallRunner(fixture.Logs);
            var run = runner.RunAsync(fixture.ScriptPath, fixture.Root, cancellation.Token);
            var pid = await fixture.ChildPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => run.WaitAsync(TimeSpan.FromSeconds(8)));
            await AssertProcessExitedAsync(pid);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private static NpmFixture CreateNpmFixture(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "DSH-IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "npm.cmd");
        File.WriteAllText(script, $"@echo off\r\n\"{typeof(HarnessMarker).Assembly.Location}\" {mode}\r\n");
        var logs = new RecentLogBuffer();
        var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        logs.LineAdded += (_, line) =>
        {
            const string prefix = "CHILD_PID=";
            if (line.Text.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(line.Text.Substring(prefix.Length), out var pid))
            {
                childPid.TrySetResult(pid);
            }
        };
        return new NpmFixture(root, script, logs, childPid);
    }

    private static DshLaunchOptions CreateOptions(string mode)
    {
        return new DshLaunchOptions
        {
            ExecutablePath = typeof(HarnessMarker).Assembly.Location,
            Arguments = [mode],
            WorkingDirectory = Path.GetTempPath(),
            FallbackUri = new Uri("http://127.0.0.1:43123/"),
        };
    }

    private static async Task AssertProcessExitedAsync(int processId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }
            await Task.Delay(100);
        }

        Assert.Fail($"Descendant process {processId} is still running.");
    }

    private sealed record NpmFixture(
        string Root,
        string ScriptPath,
        RecentLogBuffer Logs,
        TaskCompletionSource<int> ChildPid);
}
