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
        IReadOnlyList<HarnessError> errors,
        DependencyCheck? npm = null,
        DshInstallationSource dshSource = DshInstallationSource.None)
    {
        DesktopVersion = desktopVersion;
        DotNetVersion = dotNetVersion;
        WebView2 = webView2;
        GlobalDsh = globalDsh;
        Node = node;
        Npx = npx;
        Npm = npm ?? npx;
        DshSource = dshSource == DshInstallationSource.None
            && globalDsh.Status == DependencyStatus.Available
                ? DshInstallationSource.GlobalPath
                : dshSource;
        Errors = errors;
    }

    public string DesktopVersion { get; }
    public string DotNetVersion { get; }
    public DependencyCheck WebView2 { get; }
    public DependencyCheck GlobalDsh { get; }
    public DependencyCheck Node { get; }
    public DependencyCheck Npx { get; }
    public DependencyCheck Npm { get; }
    public DshInstallationSource DshSource { get; }
    public IReadOnlyList<HarnessError> Errors { get; }
    public string? WebView2RuntimeVersion => WebView2.Version;
    public string? NodeVersion => Node.Version;
    public string? NpxPath => Npx.Path;
    public string? DshPath => GlobalDsh.Path;
    public string? DshVersion => GlobalDsh.Version;
    public bool HasInstalledDsh => GlobalDsh.Status == DependencyStatus.Available
        && DshSource != DshInstallationSource.None;
    public bool CanPrepareDsh => Node.Status == DependencyStatus.Available
        && Npm.Status == DependencyStatus.Available;
    public bool RequiresDshPreparation => !HasInstalledDsh && CanPrepareDsh;
    public bool CanLaunchDsh => HasInstalledDsh || CanPrepareDsh;
}
