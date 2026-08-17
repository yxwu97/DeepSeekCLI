namespace DeepSeekHarnessDesktop.Utilities;

public static class DshPackageMetadata
{
    public const string PackageName = "@deepseek-ai/dsh";
    public const string VerifiedVersion = "0.1.0-rc.6";
    public const int DefaultPort = 3080;

    public static readonly Uri DefaultServiceUri = new("http://127.0.0.1:3080/");
    public static readonly Uri NpmLatestUri = new("https://registry.npmjs.org/@deepseek-ai%2fdsh/latest");
}
