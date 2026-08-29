namespace DeepSeekHarnessDesktop.Models;

public sealed record DshRuntimeDescriptor(
    string Version,
    int RuntimeProtocol,
    string MinimumDesktopVersion,
    string NodeVersionRange,
    string Source,
    string EvidenceSha256);
