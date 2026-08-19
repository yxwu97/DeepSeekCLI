using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Diagnostics;

namespace DeepSeekHarnessDesktop.Services;

public sealed class HarnessProcessManager : IHarnessProcessManager
{
    private readonly IRecentLogBuffer? _recentLogs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private Process? _process;
    private SuspendedProcessLaunch? _launch;
    private WindowsJobObject? _job;
    private HarnessProcessInfo? _current;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private TaskCompletionSource<ProcessExitedEventArgs>? _exitSignal;
    private bool _completionStarted;
    private bool _disposed;

    public HarnessProcessManager(IRecentLogBuffer? recentLogs = null)
    {
        _recentLogs = recentLogs;
    }

    public event EventHandler<ProcessOutputEventArgs>? OutputReceived;
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    public HarnessProcessInfo? Current
    {
        get { lock (_sync) { return _current; } }
    }

    public bool IsRunning
    {
        get { lock (_sync) { return _process is { HasExited: false }; } }
    }

    public async Task<HarnessProcessInfo> StartAsync(
        DshLaunchOptions options,
        CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HarnessProcessManager));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ValidateStart(options);
            cancellationToken.ThrowIfCancellationRequested();
            var job = new WindowsJobObject();
            SuspendedProcessLaunch? launch = null;
            try
            {
                launch = SuspendedNativeProcessLauncher.Start(options, job);
                var process = launch.Process;
                process.Exited += OnProcessExited;
                var info = new HarnessProcessInfo(process.Id, DateTimeOffset.UtcNow, options.WorkingDirectory, null);
                lock (_sync)
                {
                    _process = process;
                    _launch = launch;
                    _job = job;
                    _current = info;
                    _completionStarted = false;
                    _exitSignal = new TaskCompletionSource<ProcessExitedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _stdoutTask = ReadOutputAsync(process, launch.StandardOutput, ProcessOutputSource.StandardOutput);
                    _stderrTask = ReadOutputAsync(process, launch.StandardError, ProcessOutputSource.StandardError);
                }
                cancellationToken.ThrowIfCancellationRequested();
                launch.Resume();
                _recentLogs?.AddDesktop($"DSH 进程已创建并加入 Job Object，PID {process.Id}。");
                if (process.HasExited)
                {
                    _ = CompleteExitAsync(process);
                }
                return info;
            }
            catch (Exception exception)
            {
                ClearFailedStart(launch, job);
                if (exception is HarnessException or OperationCanceledException)
                {
                    throw;
                }
                throw new HarnessException(new HarnessError(
                    "DSH-E103",
                    "无法创建 DSH 进程",
                    exception.Message,
                    true,
                    exception));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            Process? process;
            WindowsJobObject? job;
            TaskCompletionSource<ProcessExitedEventArgs>? exitSignal;
            lock (_sync)
            {
                process = _process;
                job = _job;
                exitSignal = _exitSignal;
            }
            if (process is null)
            {
                return;
            }
            try
            {
                try
                {
                    if (!process.HasExited) job?.Dispose();
                }
                catch (InvalidOperationException)
                {
                    // Exit completion may have detached and disposed the Process after the snapshot above.
                }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                if (exitSignal is not null)
                {
                    await exitSignal.Task.WaitAsync(timeout.Token);
                }
            }
            catch (OperationCanceledException)
            {
                job?.Dispose();
                await WaitForForcedExitAsync(exitSignal);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ValidateStart(DshLaunchOptions options)
    {
        lock (_sync)
        {
            if (_process is not null)
            {
                throw new InvalidOperationException("An owned DSH process is already tracked.");
            }
        }
        if (!Directory.Exists(options.WorkingDirectory))
        {
            throw new HarnessException(new HarnessError(
                "DSH-E102",
                "工作目录不存在或不可访问",
                $"Invalid working directory: {options.WorkingDirectory}",
                false));
        }
    }

    private async Task ReadOutputAsync(Process process, Stream stream, ProcessOutputSource source)
    {
        try
        {
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync() is { } line)
            {
                PublishOutput(process, source, line);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private void PublishOutput(Process process, ProcessOutputSource source, string? value)
    {
        var line = OutputLineProcessor.Normalize(value);
        if (line is null)
        {
            return;
        }
        lock (_sync)
        {
            if (!ReferenceEquals(process, _process))
            {
                return;
            }
        }
        var output = new ProcessOutputLine(DateTimeOffset.UtcNow, source, line);
        _recentLogs?.Add(output);
        OutputReceived?.Invoke(this, new ProcessOutputEventArgs(output));
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is Process process)
        {
            _ = CompleteExitAsync(process);
        }
    }

    private async Task CompleteExitAsync(Process process)
    {
        WindowsJobObject? job;
        Task? stdoutTask;
        Task? stderrTask;
        lock (_sync)
        {
            if (!ReferenceEquals(process, _process) || _completionStarted)
            {
                return;
            }
            _completionStarted = true;
            job = _job;
            _job = null;
            stdoutTask = _stdoutTask;
            stderrTask = _stderrTask;
        }
        job?.Dispose();
        if (stdoutTask is not null && stderrTask is not null)
        {
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        CompleteExit(process);
    }

    private void CompleteExit(Process process)
    {
        SuspendedProcessLaunch? launch;
        HarnessProcessInfo? current;
        TaskCompletionSource<ProcessExitedEventArgs>? exitSignal;
        int exitCode;
        lock (_sync)
        {
            if (!ReferenceEquals(process, _process))
            {
                return;
            }
            exitCode = process.HasExited ? process.ExitCode : -1;
            current = _current;
            launch = _launch;
            exitSignal = _exitSignal;
            _process = null;
            _launch = null;
            _current = null;
            _stdoutTask = null;
            _stderrTask = null;
            _exitSignal = null;
            _completionStarted = false;
        }
        process.Exited -= OnProcessExited;
        launch?.Dispose();
        process.Dispose();
        if (current is not null)
        {
            _recentLogs?.AddDesktop($"DSH 进程 {current.ProcessId} 已退出，退出码 {exitCode}。");
            var args = new ProcessExitedEventArgs(current.ProcessId, exitCode);
            exitSignal?.TrySetResult(args);
            ProcessExited?.Invoke(this, args);
        }
    }

    private void ClearFailedStart(SuspendedProcessLaunch? launch, WindowsJobObject job)
    {
        lock (_sync)
        {
            _process = null;
            _launch = null;
            _job = null;
            _current = null;
            _stdoutTask = null;
            _stderrTask = null;
            _exitSignal = null;
            _completionStarted = false;
        }
        job.Dispose();
        launch?.Dispose();
        launch?.Process.Dispose();
    }

    private static async Task WaitForForcedExitAsync(TaskCompletionSource<ProcessExitedEventArgs>? exitSignal)
    {
        try
        {
            if (exitSignal is not null)
            {
                await exitSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch (TimeoutException exception)
        {
            throw new HarnessException(new HarnessError(
                "DSH-E206",
                "无法停止 DSH 进程树",
                "The process tree did not exit after closing its Job Object.",
                true,
                exception));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        await StopAsync(CancellationToken.None);
        _disposed = true;
        _gate.Dispose();
    }
}
