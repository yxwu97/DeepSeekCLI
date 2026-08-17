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
    private WindowsJobObject? _job;
    private HarnessProcessInfo? _current;
    private TaskCompletionSource<ProcessExitedEventArgs>? _exitSignal;
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

    public async Task<HarnessProcessInfo> StartAsync(DshLaunchOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("An owned DSH process is already running.");
            }

            if (!Directory.Exists(options.WorkingDirectory))
            {
                throw new HarnessException(new HarnessError(
                    "DSH-E102",
                    "工作目录不存在或不可访问",
                    $"Invalid working directory: {options.WorkingDirectory}",
                    false));
            }

            var extension = Path.GetExtension(options.ExecutablePath);
            var startInfo = string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
                ? CmdCommandLineBuilder.Build(options.ExecutablePath, options.Arguments, options.WorkingDirectory, options.Environment)
                : CmdCommandLineBuilder.BuildNative(options.ExecutablePath, options.Arguments, options.WorkingDirectory, options.Environment);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += OnOutputDataReceived;
            process.ErrorDataReceived += OnErrorDataReceived;
            process.Exited += OnProcessExited;

            WindowsJobObject? job = null;
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Process.Start returned false.");
                }

                job = new WindowsJobObject();
                job.Assign(process);
                var info = new HarnessProcessInfo(
                    process.Id,
                    DateTimeOffset.UtcNow,
                    options.WorkingDirectory,
                    null);
                lock (_sync)
                {
                    _process = process;
                    _job = job;
                    _current = info;
                    _exitSignal = new TaskCompletionSource<ProcessExitedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                _recentLogs?.AddDesktop($"DSH 进程已创建，PID {process.Id}。");

                if (process.HasExited)
                {
                    CompleteExit(process);
                    return info;
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                cancellationToken.ThrowIfCancellationRequested();
                return info;
            }
            catch (Exception exception)
            {
                job?.Dispose();
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                process.Dispose();
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
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
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

        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e) =>
        PublishOutput((Process)sender, ProcessOutputSource.StandardOutput, e.Data);

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e) =>
        PublishOutput((Process)sender, ProcessOutputSource.StandardError, e.Data);

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
            CompleteExit(process);
        }
    }

    private void CompleteExit(Process process)
    {
        try
        {
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // The process may already have been disposed by a concurrent completion path.
        }

        WindowsJobObject? job;
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
            job = _job;
            exitSignal = _exitSignal;
            _process = null;
            _current = null;
            _job = null;
            _exitSignal = null;
        }

        job?.Dispose();
        process.Dispose();
        if (current is not null)
        {
            _recentLogs?.AddDesktop($"DSH 进程 {current.ProcessId} 已退出，退出码 {exitCode}。");
            var args = new ProcessExitedEventArgs(current.ProcessId, exitCode);
            exitSignal?.TrySetResult(args);
            ProcessExited?.Invoke(this, args);
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
