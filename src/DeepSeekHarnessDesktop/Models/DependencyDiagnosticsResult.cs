namespace DeepSeekHarnessDesktop.Models;

public sealed record DependencyDiagnosticsResult(
    string DesktopVersion,
    string DotNetVersion,
    string? WebView2RuntimeVersion,
    string? NodeVersion,
    string? NpxPath,
    string DshVersion,
    IReadOnlyList<HarnessError> Errors);
