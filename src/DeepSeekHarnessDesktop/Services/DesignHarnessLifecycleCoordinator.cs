using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DesignHarnessLifecycleCoordinator(HarnessStateMachine stateMachine)
    : IHarnessLifecycleCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HarnessStateSnapshot Current => stateMachine.Current;

    public event EventHandler<HarnessStateSnapshot>? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await ExecuteAsync(async generation =>
        {
            await Task.Delay(250, cancellationToken);
            Commit(HarnessStateEvent.InitializationStopped, generation, "DSH 尚未启动");
        }, cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ExecuteAsync(async generation =>
        {
            var startEvent = Current.State == HarnessRuntimeState.Failed
                ? HarnessStateEvent.Retry
                : HarnessStateEvent.Start;
            if (!Commit(startEvent, generation, "正在启动 DSH"))
            {
                return;
            }

            await Task.Delay(700, cancellationToken);
            Commit(
                HarnessStateEvent.HealthReady,
                generation,
                "应用实例运行中（开发模拟）",
                new Uri("http://127.0.0.1:3080/"),
                Environment.ProcessId);
        }, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await ExecuteAsync(async generation =>
        {
            if (!Commit(HarnessStateEvent.Stop, generation, "正在停止 DSH"))
            {
                return;
            }

            await Task.Delay(400, cancellationToken);
            Commit(HarnessStateEvent.ProcessExited, generation, "DSH 已停止");
        }, cancellationToken);
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await ExecuteAsync(async generation =>
        {
            if (!Commit(HarnessStateEvent.Restart, generation, "正在重启 DSH"))
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
            Commit(HarnessStateEvent.OldProcessExited, generation, "旧进程已退出，等待地址释放");
            await Task.Delay(300, cancellationToken);
            Commit(HarnessStateEvent.OldEndpointReleased, generation, "旧地址已释放，正在启动新进程");
            await Task.Delay(500, cancellationToken);
            Commit(
                HarnessStateEvent.HealthReady,
                generation,
                "应用实例运行中（开发模拟）",
                new Uri("http://127.0.0.1:3080/"),
                Environment.ProcessId);
        }, cancellationToken);
    }

    private async Task ExecuteAsync(Func<long, Task> action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var generation = stateMachine.BeginOperation();
            await action(generation);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool Commit(
        HarnessStateEvent stateEvent,
        long generation,
        string message,
        Uri? uri = null,
        int? processId = null)
    {
        var changed = stateMachine.TryTransition(stateEvent, generation, message, uri, processId);
        if (changed)
        {
            StateChanged?.Invoke(this, Current);
        }

        return changed;
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
