namespace DeepSeekHarnessDesktop.Utilities;

public static class DshPackageMetadata
{
    public const string PackageName = "@deepseek-ai/dsh";
    public const string BootstrapVersion = "0.1.1-rc.2";
    public const string BootstrapPackageSpec = PackageName + "@" + BootstrapVersion;
    public const string ValidatedVersion = BootstrapVersion;
    public const string ValidatedPackageSpec = BootstrapPackageSpec;
    public const int RuntimeProtocol = 1;
    public const string MinimumDesktopVersion = "0.11.0";
    public const string SupportedNodeVersionRange = ">=20 <25";
    public const string BootstrapEvidenceSha256 =
        "511069f3506bb0798c7152f4297f80cf73ce19ed028d1944822cd5bc72038850";
    public const int DefaultPort = 3080;

    public static readonly Uri DefaultServiceUri = new("http://127.0.0.1:3080/");
    public static readonly Uri NpmLatestUri = new("https://registry.npmjs.org/@deepseek-ai%2fdsh/latest");
}
