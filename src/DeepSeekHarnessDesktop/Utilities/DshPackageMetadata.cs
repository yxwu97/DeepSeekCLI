namespace DeepSeekHarnessDesktop.Utilities;

public static class DshPackageMetadata
{
    public const string PackageName = "@deepseek-ai/dsh";
    public const string BootstrapVersion = "0.1.5-rc.1";
    public const string BootstrapPackageSpec = PackageName + "@" + BootstrapVersion;
    public const string ValidatedVersion = BootstrapVersion;
    public const string ValidatedPackageSpec = BootstrapPackageSpec;
    public const int RuntimeProtocol = 1;
    public const string MinimumDesktopVersion = "0.12.0";
    public const string SupportedNodeVersionRange = ">=22.19.0 <25";
    public const string BootstrapEvidenceSha256 =
        "dbe300a493fc9576e5b89b91f427eecf19f05179a490d7cf684d432e8b7ed3c9";
    public const int DefaultPort = 3080;

    public static readonly Uri DefaultServiceUri = new("http://127.0.0.1:3080/");
    public static readonly Uri NpmLatestUri = new("https://registry.npmjs.org/@deepseek-ai%2fdsh/latest");
}
