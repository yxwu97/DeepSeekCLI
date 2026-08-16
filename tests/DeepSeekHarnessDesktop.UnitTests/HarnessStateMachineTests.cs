using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class HarnessStateMachineTests
{
    [Fact]
    public void LegalTransitionTableMatchesDetailedDesign()
    {
        var expected = new HashSet<(HarnessRuntimeState, HarnessStateEvent)>
        {
            (HarnessRuntimeState.Initializing, HarnessStateEvent.DshConfirmed),
            (HarnessRuntimeState.Initializing, HarnessStateEvent.ReachableUnknown),
            (HarnessRuntimeState.Initializing, HarnessStateEvent.ExternalRedirect),
            (HarnessRuntimeState.Initializing, HarnessStateEvent.InvalidUri),
            (HarnessRuntimeState.Initializing, HarnessStateEvent.InitializationAutoStart),
            (HarnessRuntimeState.Initializing, HarnessStateEvent.InitializationStopped),
            (HarnessRuntimeState.Stopped, HarnessStateEvent.Start),
            (HarnessRuntimeState.Starting, HarnessStateEvent.PreflightDshConfirmed),
            (HarnessRuntimeState.Starting, HarnessStateEvent.PreflightReachableUnknown),
            (HarnessRuntimeState.Starting, HarnessStateEvent.PreflightExternalRedirect),
            (HarnessRuntimeState.Starting, HarnessStateEvent.PreflightInvalidUri),
            (HarnessRuntimeState.Starting, HarnessStateEvent.PreflightUnreachable),
            (HarnessRuntimeState.Starting, HarnessStateEvent.HealthReady),
            (HarnessRuntimeState.Starting, HarnessStateEvent.Stop),
            (HarnessRuntimeState.Starting, HarnessStateEvent.Cancel),
            (HarnessRuntimeState.Starting, HarnessStateEvent.ProcessExited),
            (HarnessRuntimeState.Starting, HarnessStateEvent.Timeout),
            (HarnessRuntimeState.Starting, HarnessStateEvent.Error),
            (HarnessRuntimeState.RunningOwned, HarnessStateEvent.Stop),
            (HarnessRuntimeState.RunningOwned, HarnessStateEvent.Restart),
            (HarnessRuntimeState.RunningOwned, HarnessStateEvent.ProcessExited),
            (HarnessRuntimeState.RunningExternal, HarnessStateEvent.HealthLost),
            (HarnessRuntimeState.Stopping, HarnessStateEvent.ProcessExited),
            (HarnessRuntimeState.Restarting, HarnessStateEvent.OldProcessExited),
            (HarnessRuntimeState.Restarting, HarnessStateEvent.OldEndpointReleased),
            (HarnessRuntimeState.Restarting, HarnessStateEvent.Error),
            (HarnessRuntimeState.Failed, HarnessStateEvent.Retry),
            (HarnessRuntimeState.Failed, HarnessStateEvent.Dismiss),
        };

        var actual = Enum.GetValues<HarnessRuntimeState>()
            .SelectMany(state => Enum.GetValues<HarnessStateEvent>(), (state, stateEvent) => (state, stateEvent))
            .Where(pair => HarnessStateMachine.IsLegal(pair.state, pair.stateEvent))
            .ToHashSet();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InitializingCanEnterStopped()
    {
        var machine = new HarnessStateMachine();
        var generation = machine.BeginOperation();

        var changed = machine.TryTransition(
            HarnessStateEvent.InitializationStopped,
            generation,
            "stopped");

        Assert.True(changed);
        Assert.Equal(HarnessRuntimeState.Stopped, machine.Current.State);
        Assert.Equal(generation, machine.Current.Generation);
    }

    [Theory]
    [InlineData(HarnessStateEvent.DshConfirmed, HarnessRuntimeState.RunningExternal)]
    [InlineData(HarnessStateEvent.ReachableUnknown, HarnessRuntimeState.Failed)]
    [InlineData(HarnessStateEvent.ExternalRedirect, HarnessRuntimeState.Failed)]
    [InlineData(HarnessStateEvent.InvalidUri, HarnessRuntimeState.Failed)]
    [InlineData(HarnessStateEvent.InitializationAutoStart, HarnessRuntimeState.Starting)]
    [InlineData(HarnessStateEvent.InitializationStopped, HarnessRuntimeState.Stopped)]
    public void InitializationTransitionsMatchDesign(HarnessStateEvent stateEvent, HarnessRuntimeState expected)
    {
        var machine = new HarnessStateMachine();
        var generation = machine.BeginOperation();

        Assert.True(machine.TryTransition(stateEvent, generation, "test"));
        Assert.Equal(expected, machine.Current.State);
    }

    [Fact]
    public void OwnedRestartRequiresBothGuardEvents()
    {
        var machine = CreateRunningOwned();
        var generation = machine.BeginOperation();

        Assert.True(machine.TryTransition(HarnessStateEvent.Restart, generation, "restart"));
        Assert.Equal(HarnessRuntimeState.Restarting, machine.Current.State);
        Assert.True(machine.TryTransition(HarnessStateEvent.OldProcessExited, generation, "exited"));
        Assert.Equal(HarnessRuntimeState.Restarting, machine.Current.State);
        Assert.True(machine.TryTransition(HarnessStateEvent.OldEndpointReleased, generation, "released"));
        Assert.Equal(HarnessRuntimeState.Starting, machine.Current.State);
    }

    [Fact]
    public void StaleGenerationCannotCommit()
    {
        var machine = new HarnessStateMachine();
        var staleGeneration = machine.BeginOperation();
        machine.BeginOperation();

        Assert.False(machine.TryTransition(
            HarnessStateEvent.InitializationStopped,
            staleGeneration,
            "stale"));
        Assert.Equal(HarnessRuntimeState.Initializing, machine.Current.State);
    }

    [Fact]
    public void IllegalTransitionDoesNotChangeSnapshot()
    {
        var machine = new HarnessStateMachine();
        var generation = machine.BeginOperation();
        var before = machine.Current;

        Assert.False(machine.TryTransition(HarnessStateEvent.Restart, generation, "illegal"));
        Assert.Same(before, machine.Current);
    }

    [Fact]
    public void ExternalInstanceCannotStopOrRestart()
    {
        var machine = new HarnessStateMachine();
        var generation = machine.BeginOperation();
        Assert.True(machine.TryTransition(HarnessStateEvent.DshConfirmed, generation, "external"));

        Assert.False(machine.TryTransition(HarnessStateEvent.Stop, generation, "stop"));
        Assert.False(machine.TryTransition(HarnessStateEvent.Restart, generation, "restart"));
        Assert.True(machine.TryTransition(HarnessStateEvent.HealthLost, generation, "lost"));
        Assert.Equal(HarnessRuntimeState.Stopped, machine.Current.State);
    }

    private static HarnessStateMachine CreateRunningOwned()
    {
        var machine = new HarnessStateMachine();
        var generation = machine.BeginOperation();
        Assert.True(machine.TryTransition(HarnessStateEvent.InitializationAutoStart, generation, "start"));
        Assert.True(machine.TryTransition(
            HarnessStateEvent.HealthReady,
            generation,
            "ready",
            new Uri("http://127.0.0.1:3080/"),
            42));
        return machine;
    }
}
