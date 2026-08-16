namespace DeepSeekHarnessDesktop.Models;

public enum HarnessRuntimeState
{
    Initializing,
    Stopped,
    Starting,
    RunningOwned,
    RunningExternal,
    Stopping,
    Restarting,
    Failed,
}
