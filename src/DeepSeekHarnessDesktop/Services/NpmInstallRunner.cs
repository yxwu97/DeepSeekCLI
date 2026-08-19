using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Diagnostics;
using System.Text;

namespace DeepSeekHarnessDesktop.Services;

public sealed class NpmInstallRunner : INpmInstallRunner
{
    public static readonly TimeSpan PreparationTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan NoProgressTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultOutputDrainTimeout = TimeSpan.FromSeconds(5);
    private const int MaximumCapturedErrorLines = 100;
    private readonly IRecentLogBuffer? _recentLogs;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _preparationTimeout;
    private readonly TimeSpan _noProgressTimeout;
    private readonly TimeSpan _outputDrainTimeout;

    public NpmInstallRunner(
        IRecentLogBuffer? recentLogs = null,
        TimeProvider? timeProvider = null,
        TimeSpan? preparationTimeout = null,
        TimeSpan? noProgressTimeout = null,
        TimeSpan? outputDrainTimeout = null)
    {
        _recentLogs = recentLogs;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _preparationTimeout = preparationTimeout ?? PreparationTimeout;
        _noProgressTimeout = noProgressTimeout ?? NoProgressTimeout;
        _outputDrainTimeout = outputDrainTimeout ?? DefaultOutputDrainTimeout;
    }

    public async Task RunAsync(
        string npmPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var options = new DshLaunchOptions
        {
            ExecutablePath = npmPath,
            Arguments = ["ci", "--omit=dev"],
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            FallbackUri = DshPackageMetadata.DefaultServiceUri,
            Environment = new Dictionary<string, string>(),
        };
        using var job = new WindowsJobObject();
        SuspendedProcessLaunch? launch = null;
        Task? stdoutTask = null;
        Task? stderrTask = null;
        var standardError = new Queue<string>();
        try
        {
            launch = SuspendedNativeProcessLauncher.Start(options, job);
            stdoutTask = ReadOutputAsync(launch.StandardOutput, ProcessOutputSource.StandardOutput, null);
            stderrTask = ReadOutputAsync(launch.StandardError, ProcessOutputSource.StandardError, standardError);
            var startedAt = _timeProvider.GetTimestamp();
            var lastProgressAt = startedAt;
            var progress = GetProgress(workingDirectory);
            launch.Resume();
            _recentLogs?.AddDesktop($"npm 锁定安装进程已创建并加入 Job Object，PID {launch.Process.Id}。");

            var exitTask = launch.Process.WaitForExitAsync(CancellationToken.None);
            while (!exitTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var delay = Task.Delay(ProgressInterval, cancellationToken);
                if (await Task.WhenAny(exitTask, delay) == exitTask)
                {
                    break;
                }

                var next = GetProgress(workingDirectory);
                if (next != progress)
                {
                    progress = next;
                    lastProgressAt = _timeProvider.GetTimestamp();
                }
                var now = _timeProvider.GetTimestamp();
                if (_timeProvider.GetElapsedTime(startedAt, now) >= _preparationTimeout
                    || _timeProvider.GetElapsedTime(lastProgressAt, now) >= _noProgressTimeout)
                {
                    throw new HarnessException(new HarnessError(
                        "DSH-E221",
                        "DSH 下载或安装超时，请检查网络、npm registry 和安装日志",
                        "Locked npm installation exceeded its preparation or no-progress deadline.",
                        true));
                }
            }

            await exitTask;
            await DrainOutputAfterExitAsync(stdoutTask, stderrTask);
            if (launch.Process.ExitCode != 0)
            {
                throw new HarnessException(
                    NpmFailureClassifier.Classify(standardError)
                    ?? new HarnessError(
                        "DSH-E201",
                        "DSH 安装进程异常退出",
                        $"npm ci exited with code {launch.Process.ExitCode}.",
                        true));
            }
        }
        finally
        {
            if (launch is not null)
            {
                await CleanupLaunchAsync(launch, job, stdoutTask, stderrTask);
            }
        }

        async Task ReadOutputAsync(
            Stream stream,
            ProcessOutputSource source,
            Queue<string>? capture)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
            while (await reader.ReadLineAsync() is { } line)
            {
                var normalized = OutputLineProcessor.Normalize(line);
                if (normalized is null)
                {
                    continue;
                }
                if (capture is not null)
                {
                    if (capture.Count == MaximumCapturedErrorLines)
                    {
                        capture.Dequeue();
                    }
                    capture.Enqueue(normalized);
                }
                _recentLogs?.Add(new ProcessOutputLine(DateTimeOffset.Now, source, normalized));
            }
        }
    }

    private async Task DrainOutputAfterExitAsync(Task stdoutTask, Task stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(_outputDrainTimeout);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or TimeoutException)
        {
            throw new HarnessException(new HarnessError(
                "DSH-E201",
                "DSH 安装进程未能正常结束",
                $"npm output pipes did not close cleanly: {exception.GetType().Name}.",
                true,
                exception));
        }
    }

    private async Task CleanupLaunchAsync(
        SuspendedProcessLaunch launch,
        WindowsJobObject job,
        Task? stdoutTask,
        Task? stderrTask)
    {
        var process = launch.Process;
        job.Dispose();
        await WaitForRootExitAsync(process);

        var outputCompletion = stdoutTask is not null && stderrTask is not null
            ? Task.WhenAll(stdoutTask, stderrTask)
            : Task.CompletedTask;
        if (await WaitForOutputCompletionAsync(outputCompletion))
        {
            launch.Dispose();
            process.Dispose();
            return;
        }

        _ = outputCompletion.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                launch.Dispose();
                process.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task WaitForRootExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            try { process.Kill(); }
            catch (InvalidOperationException)
            {
                _recentLogs?.AddDesktop("npm 安装根进程在强制终止前已退出。");
            }
            try
            {
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                _recentLogs?.AddDesktop("npm 安装根进程在清理期限内未报告退出。");
            }
        }
    }

    private async Task<bool> WaitForOutputCompletionAsync(Task outputCompletion)
    {
        try
        {
            await outputCompletion.WaitAsync(TimeSpan.FromSeconds(2));
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException)
        {
            _recentLogs?.AddDesktop($"npm 安装输出管道已结束：{exception.GetType().Name}。");
            return true;
        }
        catch (TimeoutException)
        {
            _recentLogs?.AddDesktop("npm 安装输出管道将在后台完成清理。");
            return false;
        }
    }

    private static InstallProgress GetProgress(string root)
    {
        try
        {
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
            long bytes = 0;
            var count = 0;
            foreach (var file in files)
            {
                count++;
                try { bytes += new FileInfo(file).Length; }
                catch (IOException) { }
            }
            return new InstallProgress(count, bytes);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return default;
        }
    }

    private readonly record struct InstallProgress(int FileCount, long Bytes);
}
