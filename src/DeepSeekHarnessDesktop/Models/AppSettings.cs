namespace DeepSeekHarnessDesktop.Models;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string WorkspacePath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public Uri ServiceUri { get; set; } = Utilities.DshPackageMetadata.DefaultServiceUri;
    public bool AutoStart { get; set; } = true;
    public int StartupTimeoutSeconds { get; set; } = 60;
    public LaunchSettings Launch { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public WebViewSettings WebView { get; set; } = new();
}

public sealed record LaunchSettings
{
    public LaunchMode Mode { get; init; } = LaunchMode.Auto;
    public string? ExecutablePath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

public enum LaunchMode
{
    Auto,
    Custom,
}

public sealed record WindowSettings
{
    public double? Left { get; init; }
    public double? Top { get; init; }
    public double Width { get; init; } = 1280;
    public double Height { get; init; } = 820;
    public bool IsMaximized { get; init; }
}

public sealed record WebViewSettings
{
    public double ZoomFactor { get; init; } = 1.0;
    public bool AllowDevTools { get; init; }
}
