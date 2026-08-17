namespace DeepSeekHarnessDesktop.Models;

public sealed record DependencyDiagnosticsResult
{
    public DependencyDiagnosticsResult(
        string desktopVersion,
        string dotNetVersion,
        DependencyCheck webView2,
        DependencyCheck globalDsh,
        DependencyCheck node,
        DependencyCheck npx,
        IReadOnlyList<HarnessError> errors)
    {
        DesktopVersion = desktopVersion;
        DotNetVersion = dotNetVersion;
        WebView2 = webView2;
        GlobalDsh = globalDsh;
        Node = node;
        Npx = npx;
        Errors = errors;
    }

    public string DesktopVersion { get; }
    public string DotNetVersion { get; }
    public DependencyCheck WebView2 { get; }
    public DependencyCheck GlobalDsh { get; }
    public DependencyCheck Node { get; }
    public DependencyCheck Npx { get; }
    public IReadOnlyList<HarnessError> Errors { get; }
    public string? WebView2RuntimeVersion => WebView2.Version;
    public string? NodeVersion => Node.Version;
    public string? NpxPath => Npx.Path;
    public string? DshPath => GlobalDsh.Path;
    public string? DshVersion => GlobalDsh.Version;
    public bool CanLaunchDsh => GlobalDsh.Status == DependencyStatus.Available
        || (Node.Status == DependencyStatus.Available && Npx.Status == DependencyStatus.Available);
}
