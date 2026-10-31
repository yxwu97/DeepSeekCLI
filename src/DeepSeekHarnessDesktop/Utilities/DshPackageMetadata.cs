namespace DeepSeekHarnessDesktop.Utilities;

public static class DshPackageMetadata
{
    public const string PackageName = "@deepseek-ai/dsh";
    public const string BootstrapVersion = "0.2.1-rc.2";
    public const string BootstrapPackageSpec = PackageName + "@" + BootstrapVersion;
    public const string ValidatedVersion = BootstrapVersion;
    public const string ValidatedPackageSpec = BootstrapPackageSpec;
    public const int RuntimeProtocol = 1;
    public const string MinimumDesktopVersion = "0.13.6";
    public const string SupportedNodeVersionRange = ">=22.19.0 <25";
    public const string BootstrapEvidenceSha256 =
        "9134941acfc4105d52d3cb2436b0ffda8508b7d7ec37e23400d46f60236bb327";
    public const int DefaultPort = 3080;

    public static readonly Uri DefaultServiceUri = new("http://127.0.0.1:3080/");
    public static readonly Uri NpmLatestUri = new("https://registry.npmjs.org/@deepseek-ai%2fdsh/latest");
}
