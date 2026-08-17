using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.Services;

public sealed class HarnessLifecycleCoordinator : IHarnessLifecycleCoordinator
{
    private readonly HarnessStateMachine _stateMachine;
    private readonly IDshCommandResolver _resolver;
    private readonly IHarnessProcessManager _processManager;
    private readonly IHarnessHealthMonitor _healthMonitor;
    private readonly IRuntimeHealthWatcher? _runtimeHealthWatcher;
    private readonly ISettingsService? _settingsService;
    private readonly AppSettings _settings;
    private readonly IRecentLogBuffer? _recentLogs;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _operationSync = new();
    private readonly object _ownedProcessSync = new();
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _runtimeWatchCts;
    private Task? _runtimeWatchTask;
    private int? _ownedProcessId;
    private long? _ownedProcessGeneration;
    private ProcessExitedEventArgs? _pendingProcessExit;
    private bool _awaitingProcessRegistration;
    private Uri? _reportedUri;
    private DateTimeOffset _ownedLaunchStartedAt;
    private bool _ownedLaunchUsesNpx;
    private int _startRequested;
    private bool _disposed;

    public HarnessLifecycleCoordinator(
        HarnessStateMachine stateMachine,
        IDshCommandResolver resolver,
        IHarnessProcessManager processManager,
        IHarnessHealthMonitor healthMonitor,
        AppSettings settings,
        IRuntimeHealthWatcher? runtimeHealthWatcher = null,
        ISettingsService? settingsService = null,
        IRecentLogBuffer? recentLogs = null)
    {
        _stateMachine = stateMachine;
        _resolver = resolver;
        _processManager = processManager;
        _healthMonitor = healthMonitor;
        _settings = settings;
        _runtimeHealthWatcher = runtimeHealthWatcher;
        _settingsService = settingsService;
        _recentLogs = recentLogs;
        _processManager.OutputReceived += OnOutputReceived;
        _processManager.ProcessExited += OnProcessExited;
    }

    public HarnessStateSnapshot Current => _stateMachine.Current;
    public event EventHandler<HarnessStateSnapshot>? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RunOperationAsync(async (generation, token) =>
        {
            var probe = await _healthMonitor.ProbeAsync(_settings.ServiceUri, TimeSpan.FromSeconds(2), token);
            token.ThrowIfCancellationRequested();
            switch (probe.Status)
            {
                case HealthProbeStatus.DshConfirmed:
                    var externalUri = NormalizeConfirmedUri(probe);
                    if (Commit(HarnessStateEvent.DshConfirmed, generation, "外部 DSH 实例运行中", externalUri))
                    {
                        StartRuntimeWatcher(externalUri, generation);
                    }
                    break;
                case HealthProbeStatus.ReachableUnknown:
                    Commit(HarnessStateEvent.ReachableUnknown, generation, "检测到无法确认身份的本机服务", error: Error205(probe.Detail));
                    break;
                case HealthProbeStatus.ExternalRedirect:
                    Commit(HarnessStateEvent.ExternalRedirect, generation, "服务重定向到不允许的地址", error: new HarnessError("DSH-E204", "服务重定向到不允许的地址", probe.Detail ?? string.Empty, false));
                    break;
                case HealthProbeStatus.InvalidUri:
                    Commit(HarnessStateEvent.InvalidUri, generation, "服务地址无效", error: new HarnessError("DSH-E202", "服务地址无效", probe.Detail ?? string.Empty, true));
                    break;
                case HealthProbeStatus.Unreachable when _settings.AutoStart:
                    Commit(HarnessStateEvent.InitializationAutoStart, generation, "正在创建 DSH 进程");
                    try
                    {
                        await StartOwnedAsync(generation, token);
                    }
                    catch (HarnessException exception)
                    {
                        await FailStartingAsync(generation, exception.Error);
                    }
                    break;
                case HealthProbeStatus.Unreachable:
                    Commit(HarnessStateEvent.InitializationStopped, generation, "DSH 尚未启动");
                    break;
            }
        }, cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _startRequested, 1) == 1)
        {
            return;
        }

        try
        {
            await RunOperationAsync(async (generation, token) =>
            {
                var startEvent = Current.State == HarnessRuntimeState.Failed
                    ? HarnessStateEvent.Retry
                    : HarnessStateEvent.Start;
                if (!Commit(startEvent, generation, "正在检查本机 DSH 服务"))
                {
                    return;
                }

                try
                {
                    var preflight = await _healthMonitor.ProbeAsync(_settings.ServiceUri, TimeSpan.FromSeconds(2), token);
                    token.ThrowIfCancellationRequested();
                    if (preflight.Status != HealthProbeStatus.Unreachable)
                    {
                        CommitPreflight(preflight, generation);
                        return;
                    }

                    Commit(HarnessStateEvent.PreflightUnreachable, generation, "正在创建 DSH 进程");
                    await StartOwnedAsync(generation, token);
                }
                catch (OperationCanceledException)
                {
                    await CancelStartingAsync(generation);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }
                catch (HarnessException exception)
                {
                    await FailStartingAsync(generation, exception.Error);
                }
                catch (Exception exception)
                {
                    await FailStartingAsync(generation, new HarnessError(
                        "DSH-E103", "无法创建 DSH 进程", exception.Message, true, exception));
                }
            }, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _startRequested, 0);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await RunOperationAsync(async (generation, token) =>
        {
            if (Current.State is not (HarnessRuntimeState.Starting or HarnessRuntimeState.RunningOwned))
            {
                return;
            }

            if (!Commit(HarnessStateEvent.Stop, generation, "正在停止 DSH"))
            {
                return;
            }

            UntrackOwnedProcess();
            await _processManager.StopAsync(token);
            Commit(HarnessStateEvent.ProcessExited, generation, "DSH 已停止");
        }, cancellationToken);
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await RunOperationAsync(async (generation, token) =>
        {
            await RestartOwnedAsync(generation, token);
        }, cancellationToken);
    }

    public async Task ApplyServiceUriAsync(Uri serviceUri, CancellationToken cancellationToken)
    {
        if (!ServiceUriValidator.TryNormalize(serviceUri, out var normalized, out var validationError))
        {
            throw new HarnessException(new HarnessError("DSH-E202", "服务地址无效", validationError, true));
        }

        await RunOperationAsync(async (generation, token) =>
        {
            switch (Current.State)
            {
                case HarnessRuntimeState.Stopped:
                case HarnessRuntimeState.Failed:
                    await SaveServiceUriAsync(normalized, token);
                    break;
                case HarnessRuntimeState.RunningExternal:
                    await SwitchExternalUriAsync(normalized, generation, token);
                    break;
                case HarnessRuntimeState.RunningOwned:
                    await SaveServiceUriAsync(normalized, token);
                    await RestartOwnedAsync(generation, token);
                    break;
                default:
                    throw new HarnessException(new HarnessError(
                        "DSH-E207",
                        "当前正在执行生命周期操作，暂时不能修改服务地址",
                        $"Service URI cannot be applied while state is {Current.State}.",
                        true));
            }
        }, cancellationToken, cancelRuntimeWatcher: false);
    }

    private async Task RestartOwnedAsync(long generation, CancellationToken token)
    {
        if (!Commit(HarnessStateEvent.Restart, generation, "正在停止旧 DSH 进程"))
        {
            return;
        }

        var oldUri = Current.ServiceUri ?? _settings.ServiceUri;
        try
        {
            UntrackOwnedProcess();
            await _processManager.StopAsync(token);
            Commit(HarnessStateEvent.OldProcessExited, generation, "旧进程已退出，等待地址释放");
            await WaitForEndpointReleaseAsync(oldUri, token);
            Commit(HarnessStateEvent.OldEndpointReleased, generation, "旧地址已释放，正在启动新进程");
            await StartOwnedAsync(generation, token);
        }
        catch (OperationCanceledException)
        {
            await CancelRestartAsync(generation);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = exception is HarnessException harnessException
                ? harnessException.Error
                : new HarnessError("DSH-E103", "无法重启 DSH", exception.Message, true, exception);
            Commit(HarnessStateEvent.Error, generation, error.UserMessage, error: error);
        }
    }

    private async Task CancelRestartAsync(long generation)
    {
        if (Current.Generation != generation
            || Current.State is not (HarnessRuntimeState.Restarting or HarnessRuntimeState.Starting))
        {
            return;
        }

        Commit(HarnessStateEvent.Cancel, generation, "正在取消重启");
        UntrackOwnedProcess();
        await _processManager.StopAsync(CancellationToken.None);
        Commit(HarnessStateEvent.ProcessExited, generation, "DSH 已停止");
    }

    private async Task WaitForEndpointReleaseAsync(Uri oldUri, CancellationToken token)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await _healthMonitor.ProbeAsync(oldUri, TimeSpan.FromSeconds(2), token);
            token.ThrowIfCancellationRequested();
            if (result.Status != HealthProbeStatus.Unreachable)
            {
                throw new HarnessException(new HarnessError(
                    "DSH-E205", "端口被其他服务占用", $"Old endpoint remains occupied: {result.Status}", true));
            }

            if (attempt == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), token);
            }
        }
    }

    private async Task SwitchExternalUriAsync(Uri serviceUri, long generation, CancellationToken token)
    {
        var oldUri = Current.ServiceUri ?? _settings.ServiceUri;
        try
        {
            var probe = await _healthMonitor.ProbeAsync(serviceUri, TimeSpan.FromSeconds(5), token);
            token.ThrowIfCancellationRequested();
            if (probe.Status != HealthProbeStatus.DshConfirmed)
            {
                throw CreateProbeException(probe);
            }

            var confirmedUri = NormalizeConfirmedUri(probe);
            await SaveServiceUriAsync(confirmedUri, token);
            CancelRuntimeWatcher();
            Commit(HarnessStateEvent.ExternalAddressChanged, generation, "已切换外部 DSH 实例", confirmedUri);
            StartRuntimeWatcher(confirmedUri, generation);
        }
        catch
        {
            CancelRuntimeWatcher();
            StartRuntimeWatcher(oldUri, generation);
            throw;
        }
    }

    private async Task SaveServiceUriAsync(Uri serviceUri, CancellationToken token)
    {
        var oldUri = _settings.ServiceUri;
        _settings.ServiceUri = serviceUri;
        try
        {
            if (_settingsService is not null)
            {
                await _settingsService.SaveAsync(_settings, token);
            }
        }
        catch
        {
            _settings.ServiceUri = oldUri;
            throw;
        }
    }

    private static HarnessException CreateProbeException(HealthProbeResult probe) => probe.Status switch
    {
        HealthProbeStatus.ReachableUnknown => new HarnessException(Error205(probe.Detail)),
        HealthProbeStatus.ExternalRedirect => new HarnessException(new HarnessError("DSH-E204", "服务重定向到不允许的地址", probe.Detail ?? string.Empty, false)),
        HealthProbeStatus.InvalidUri => new HarnessException(new HarnessError("DSH-E202", "服务地址无效", probe.Detail ?? string.Empty, true)),
        _ => new HarnessException(new HarnessError("DSH-E208", "无法连接到指定的 DSH 服务", probe.Detail ?? "Service is unreachable.", true)),
    };

    private static Uri NormalizeConfirmedUri(HealthProbeResult probe) =>
        ServiceUriValidator.NormalizeOrThrow(probe.FinalUri ?? probe.RequestedUri);

    private async Task StartOwnedAsync(long generation, CancellationToken token)
    {
        _reportedUri = null;
        var options = await _resolver.ResolveAsync(_settings, token);
        _recentLogs?.AddDesktop(
            $"启动命令：{LaunchCommandLogFormatter.Format(options)}；"
            + $"工作目录：{options.WorkingDirectory}；目标：{options.FallbackUri}；最长等待：{options.StartupTimeout.TotalMinutes:0} 分钟。");
        PrepareOwnedProcessTracking(generation, options);
        HarnessProcessInfo process;
        try
        {
            process = await _processManager.StartAsync(options, token);
        }
        catch
        {
            UntrackOwnedProcess();
            throw;
        }
        TrackOwnedProcess(process, generation);
        token.ThrowIfCancellationRequested();
        _recentLogs?.AddDesktop("进程已启动，正在等待本机 DSH 服务通过身份验证。");
        var ready = await _healthMonitor.WaitUntilReadyAsync(
            () => _reportedUri ?? options.FallbackUri,
            options.StartupTimeout,
            token);
        token.ThrowIfCancellationRequested();
        if (ready.Status != HealthProbeStatus.DshConfirmed || ready.FinalUri is null)
        {
            var error = ready.Status switch
            {
                HealthProbeStatus.ExternalRedirect => new HarnessError("DSH-E204", "服务重定向到不允许的地址", ready.Detail ?? "External redirect.", false),
                HealthProbeStatus.ReachableUnknown => new HarnessError("DSH-E205", "端口被其他服务占用", ready.Detail ?? "Unknown service.", true),
                HealthProbeStatus.InvalidUri => new HarnessError("DSH-E202", "服务地址无效", ready.Detail ?? "Invalid URI.", true),
                _ => new HarnessError("DSH-E203", "DSH 启动超时", ready.Detail ?? "Startup timeout.", true),
            };
            throw new HarnessException(error);
        }

        Commit(HarnessStateEvent.HealthReady, generation, "应用实例运行中", NormalizeConfirmedUri(ready), process.ProcessId);
    }

    private void CommitPreflight(HealthProbeResult result, long generation)
    {
        switch (result.Status)
        {
            case HealthProbeStatus.DshConfirmed:
                var externalUri = NormalizeConfirmedUri(result);
                if (Commit(HarnessStateEvent.PreflightDshConfirmed, generation, "外部 DSH 实例运行中", externalUri))
                {
                    StartRuntimeWatcher(externalUri, generation);
                }
                break;
            case HealthProbeStatus.ReachableUnknown:
                Commit(HarnessStateEvent.PreflightReachableUnknown, generation, "检测到无法确认身份的本机服务", error: Error205(result.Detail));
                break;
            case HealthProbeStatus.ExternalRedirect:
                Commit(HarnessStateEvent.PreflightExternalRedirect, generation, "服务重定向到不允许的地址", error: new HarnessError("DSH-E204", "服务重定向到不允许的地址", result.Detail ?? string.Empty, false));
                break;
            case HealthProbeStatus.InvalidUri:
                Commit(HarnessStateEvent.PreflightInvalidUri, generation, "服务地址无效", error: new HarnessError("DSH-E202", "服务地址无效", result.Detail ?? string.Empty, true));
                break;
        }
    }

    private async Task CancelStartingAsync(long generation)
    {
        if (Current.Generation == generation && Current.State == HarnessRuntimeState.Starting)
        {
            Commit(HarnessStateEvent.Cancel, generation, "正在取消启动");
            UntrackOwnedProcess();
            await _processManager.StopAsync(CancellationToken.None);
            Commit(HarnessStateEvent.ProcessExited, generation, "DSH 已停止");
        }
    }

    private async Task FailStartingAsync(long generation, HarnessError error)
    {
        UntrackOwnedProcess();
        await _processManager.StopAsync(CancellationToken.None);
        if (Current.Generation == generation && Current.State == HarnessRuntimeState.Starting)
        {
            var stateEvent = error.Code == "DSH-E203" ? HarnessStateEvent.Timeout : HarnessStateEvent.Error;
            Commit(stateEvent, generation, error.UserMessage, error: error);
        }
    }

    private async Task RunOperationAsync(
        Func<long, CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool cancelRuntimeWatcher = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_operationSync)
        {
            _operationCts?.Cancel();
            if (cancelRuntimeWatcher)
            {
                _runtimeWatchCts?.Cancel();
            }
        }

        await _lifecycleGate.WaitAsync(cancellationToken);
        CancellationTokenSource operationCts;
        try
        {
            lock (_operationSync)
            {
                _operationCts?.Dispose();
                _operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                operationCts = _operationCts;
            }

            var generation = _stateMachine.BeginOperation();
            await operation(generation, operationCts.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private bool Commit(
        HarnessStateEvent stateEvent,
        long generation,
        string message,
        Uri? uri = null,
        int? processId = null,
        HarnessError? error = null)
    {
        var changed = _stateMachine.TryTransition(stateEvent, generation, message, uri, processId, error);
        if (changed)
        {
            var suffix = Current.Error is null ? string.Empty : $"（{Current.Error.Code}）";
            _recentLogs?.AddDesktop($"{Current.StatusMessage}{suffix}");
            StateChanged?.Invoke(this, Current);
        }

        return changed;
    }

    private void OnOutputReceived(object? sender, ProcessOutputEventArgs e)
    {
        var uri = UrlParser.TryParseLoopback(e.Line.Text);
        if (uri is not null && Current.State is HarnessRuntimeState.Starting or HarnessRuntimeState.Restarting)
        {
            _reportedUri = uri;
        }
    }

    private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        long? generation = null;
        lock (_ownedProcessSync)
        {
            if (_ownedProcessId == e.ProcessId && _ownedProcessGeneration is { } trackedGeneration)
            {
                generation = trackedGeneration;
                _ownedProcessId = null;
                _ownedProcessGeneration = null;
            }
            else if (_awaitingProcessRegistration)
            {
                _pendingProcessExit = e;
            }
        }

        if (generation is { } value)
        {
            HandleUnexpectedProcessExit(e, value);
        }
    }

    private void TrackOwnedProcess(HarnessProcessInfo process, long generation)
    {
        ProcessExitedEventArgs? pendingExit = null;
        lock (_ownedProcessSync)
        {
            _awaitingProcessRegistration = false;
            _ownedProcessId = process.ProcessId;
            _ownedProcessGeneration = generation;
            if (_pendingProcessExit?.ProcessId == process.ProcessId)
            {
                pendingExit = _pendingProcessExit;
                _ownedProcessId = null;
                _ownedProcessGeneration = null;
            }
            _pendingProcessExit = null;
        }

        if (pendingExit is not null)
        {
            HandleUnexpectedProcessExit(pendingExit, generation);
        }
    }

    private void PrepareOwnedProcessTracking(long generation, DshLaunchOptions options)
    {
        lock (_ownedProcessSync)
        {
            _awaitingProcessRegistration = true;
            _ownedProcessId = null;
            _ownedProcessGeneration = generation;
            _pendingProcessExit = null;
            _ownedLaunchStartedAt = DateTimeOffset.UtcNow;
            _ownedLaunchUsesNpx = string.Equals(
                Path.GetFileName(options.ExecutablePath),
                "npx.cmd",
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UntrackOwnedProcess()
    {
        lock (_ownedProcessSync)
        {
            _awaitingProcessRegistration = false;
            _ownedProcessId = null;
            _ownedProcessGeneration = null;
            _pendingProcessExit = null;
            _ownedLaunchStartedAt = default;
            _ownedLaunchUsesNpx = false;
        }
    }

    private void HandleUnexpectedProcessExit(ProcessExitedEventArgs e, long generation)
    {
        var error = ClassifyUnexpectedExit(e);
        if (!Commit(HarnessStateEvent.ProcessExited, generation, error.UserMessage, error: error))
        {
            return;
        }

        lock (_operationSync)
        {
            _operationCts?.Cancel();
        }
    }

    private HarnessError ClassifyUnexpectedExit(ProcessExitedEventArgs e)
    {
        DateTimeOffset startedAt;
        bool usesNpx;
        lock (_ownedProcessSync)
        {
            startedAt = _ownedLaunchStartedAt;
            usesNpx = _ownedLaunchUsesNpx;
        }

        var classified = usesNpx
            ? NpmFailureClassifier.Classify(
                _recentLogs?.Snapshot()
                    .Where(line => line.Source == ProcessOutputSource.StandardError && line.Timestamp >= startedAt)
                    .Select(line => line.Text)
                ?? [])
            : null;
        return classified ?? new HarnessError(
            "DSH-E201",
            "DSH 进程意外退出",
            $"Process {e.ProcessId} exited with code {e.ExitCode}.",
            true);
    }

    private static HarnessError Error205(string? detail) =>
        new("DSH-E205", "端口被其他服务占用", detail ?? "Reachable service is not DSH.", true);

    private void StartRuntimeWatcher(Uri uri, long generation)
    {
        if (_runtimeHealthWatcher is null)
        {
            return;
        }

        lock (_operationSync)
        {
            _runtimeWatchCts?.Cancel();
            _runtimeWatchCts?.Dispose();
            _runtimeWatchCts = new CancellationTokenSource();
            _runtimeWatchTask = MonitorExternalAsync(uri, generation, _runtimeWatchCts.Token);
        }
    }

    private void CancelRuntimeWatcher()
    {
        lock (_operationSync)
        {
            _runtimeWatchCts?.Cancel();
            _runtimeWatchCts?.Dispose();
            _runtimeWatchCts = null;
            _runtimeWatchTask = null;
        }
    }

    private async Task MonitorExternalAsync(Uri uri, long generation, CancellationToken cancellationToken)
    {
        try
        {
            var lost = await _runtimeHealthWatcher!.WatchAsync(uri, generation, cancellationToken);
            if (lost is null)
            {
                return;
            }

            await _lifecycleGate.WaitAsync(cancellationToken);
            try
            {
                if (Current.Generation == generation && Current.State == HarnessRuntimeState.RunningExternal)
                {
                    Commit(
                        HarnessStateEvent.HealthLost,
                        generation,
                        lost.Error?.UserMessage ?? "外部 DSH 已不可访问",
                        error: lost.Error);
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        lock (_operationSync)
        {
            _operationCts?.Cancel();
            _runtimeWatchCts?.Cancel();
        }

        await _lifecycleGate.WaitAsync();
        try
        {
            UntrackOwnedProcess();
            await _processManager.StopAsync(CancellationToken.None);
            if (_runtimeWatchTask is not null)
            {
                try
                {
                    await _runtimeWatchTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
            _processManager.OutputReceived -= OnOutputReceived;
            _processManager.ProcessExited -= OnProcessExited;
            _operationCts?.Dispose();
            _runtimeWatchCts?.Dispose();
            _disposed = true;
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }
}
