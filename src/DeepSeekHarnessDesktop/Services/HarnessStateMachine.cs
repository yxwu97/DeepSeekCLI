using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services;

public sealed class HarnessStateMachine
{
    private static readonly IReadOnlyDictionary<(HarnessRuntimeState, HarnessStateEvent), HarnessRuntimeState> Transitions =
        new Dictionary<(HarnessRuntimeState, HarnessStateEvent), HarnessRuntimeState>
        {
            [(HarnessRuntimeState.Initializing, HarnessStateEvent.DshConfirmed)] = HarnessRuntimeState.RunningExternal,
            [(HarnessRuntimeState.Initializing, HarnessStateEvent.ReachableUnknown)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Initializing, HarnessStateEvent.ExternalRedirect)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Initializing, HarnessStateEvent.InvalidUri)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Initializing, HarnessStateEvent.InitializationAutoStart)] = HarnessRuntimeState.Starting,
            [(HarnessRuntimeState.Initializing, HarnessStateEvent.InitializationStopped)] = HarnessRuntimeState.Stopped,
            [(HarnessRuntimeState.Stopped, HarnessStateEvent.Start)] = HarnessRuntimeState.Starting,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.PreflightDshConfirmed)] = HarnessRuntimeState.RunningExternal,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.PreflightReachableUnknown)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.PreflightExternalRedirect)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.PreflightInvalidUri)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.PreflightUnreachable)] = HarnessRuntimeState.Starting,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.HealthReady)] = HarnessRuntimeState.RunningOwned,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.Stop)] = HarnessRuntimeState.Stopping,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.Cancel)] = HarnessRuntimeState.Stopping,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.ProcessExited)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.Timeout)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Starting, HarnessStateEvent.Error)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.RunningOwned, HarnessStateEvent.Stop)] = HarnessRuntimeState.Stopping,
            [(HarnessRuntimeState.RunningOwned, HarnessStateEvent.Restart)] = HarnessRuntimeState.Restarting,
            [(HarnessRuntimeState.RunningOwned, HarnessStateEvent.ProcessExited)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.RunningExternal, HarnessStateEvent.HealthLost)] = HarnessRuntimeState.Stopped,
            [(HarnessRuntimeState.RunningExternal, HarnessStateEvent.ExternalAddressChanged)] = HarnessRuntimeState.RunningExternal,
            [(HarnessRuntimeState.Stopping, HarnessStateEvent.ProcessExited)] = HarnessRuntimeState.Stopped,
            [(HarnessRuntimeState.Restarting, HarnessStateEvent.OldProcessExited)] = HarnessRuntimeState.Restarting,
            [(HarnessRuntimeState.Restarting, HarnessStateEvent.OldEndpointReleased)] = HarnessRuntimeState.Starting,
            [(HarnessRuntimeState.Restarting, HarnessStateEvent.Error)] = HarnessRuntimeState.Failed,
            [(HarnessRuntimeState.Restarting, HarnessStateEvent.Cancel)] = HarnessRuntimeState.Stopping,
            [(HarnessRuntimeState.Failed, HarnessStateEvent.Retry)] = HarnessRuntimeState.Starting,
            [(HarnessRuntimeState.Failed, HarnessStateEvent.Dismiss)] = HarnessRuntimeState.Stopped,
        };

    private readonly object _sync = new();

    public HarnessStateMachine()
    {
        Current = new HarnessStateSnapshot(
            HarnessRuntimeState.Initializing,
            null,
            null,
            false,
            null,
            "正在初始化桌面宿主",
            DateTimeOffset.UtcNow,
            0);
    }

    public HarnessStateSnapshot Current { get; private set; }

    public long BeginOperation()
    {
        lock (_sync)
        {
            Current = Current with
            {
                ChangedAt = DateTimeOffset.UtcNow,
                Generation = Current.Generation + 1,
            };
            return Current.Generation;
        }
    }

    public bool TryTransition(
        HarnessStateEvent stateEvent,
        long generation,
        string statusMessage,
        Uri? serviceUri = null,
        int? processId = null,
        HarnessError? error = null)
    {
        lock (_sync)
        {
            if (generation != Current.Generation
                || !Transitions.TryGetValue((Current.State, stateEvent), out var nextState))
            {
                return false;
            }

            var owned = nextState == HarnessRuntimeState.RunningOwned
                || (nextState is HarnessRuntimeState.Stopping or HarnessRuntimeState.Restarting
                    && Current.IsOwned);
            Current = new HarnessStateSnapshot(
                nextState,
                serviceUri ?? Current.ServiceUri,
                processId ?? (owned ? Current.ProcessId : null),
                owned,
                error,
                statusMessage,
                DateTimeOffset.UtcNow,
                generation);
            return true;
        }
    }

    public static bool IsLegal(HarnessRuntimeState state, HarnessStateEvent stateEvent) =>
        Transitions.ContainsKey((state, stateEvent));
}
