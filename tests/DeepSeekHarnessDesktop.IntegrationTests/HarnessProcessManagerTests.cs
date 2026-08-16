using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.TestHarness;

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
