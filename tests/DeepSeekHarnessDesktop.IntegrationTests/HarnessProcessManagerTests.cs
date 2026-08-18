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
                && int.TryParse(args.Line.Text[prefix.Length..], out var pid))
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
                typeof(HarnessMarker).Assembly.Location,
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
    public async Task SuspendedLauncherExecutesValidatedNpxCmdAndReapsProcess()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DSH-IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "npx.cmd");
            await File.WriteAllTextAsync(script, """
                @echo off
                if not "%~1"=="-y" exit /b 41
                if not "%~2"=="@deepseek-ai/dsh@0.1.0-rc.6" exit /b 42
                if not "%~3"=="web" exit /b 43
                if not "%~4"=="" exit /b 44
                echo NPX_CMD_OK
                exit /b 0
                """);

            await using var manager = new HarnessProcessManager();
            var output = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var exited = new TaskCompletionSource<ProcessExitedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.OutputReceived += (_, args) =>
            {
                if (args.Line.Text == "NPX_CMD_OK")
                {
                    output.TrySetResult(args.Line.Text);
                }
            };
            manager.ProcessExited += (_, args) => exited.TrySetResult(args);
            var options = new DshLaunchOptions
            {
                ExecutablePath = script,
                Arguments = ["-y", DshPackageMetadata.ValidatedPackageSpec, "web"],
                WorkingDirectory = directory,
                FallbackUri = new Uri("http://127.0.0.1:43123/"),
            };

            var info = await manager.StartAsync(options, CancellationToken.None);
            Assert.Equal("NPX_CMD_OK", await output.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            var result = await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(info.ProcessId, result.ProcessId);
            Assert.Equal(0, result.ExitCode);
            Assert.False(manager.IsRunning);
            Assert.Null(manager.Current);
            await AssertProcessExitedAsync(info.ProcessId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DshLaunchOptions CreateOptions(string mode)
    {
        var dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        return new DshLaunchOptions
        {
            ExecutablePath = dotnet,
            Arguments = [typeof(HarnessMarker).Assembly.Location, mode],
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
}
