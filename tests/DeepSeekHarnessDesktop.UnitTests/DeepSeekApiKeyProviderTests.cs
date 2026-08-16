using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class DeepSeekApiKeyProviderTests
{
    [Fact]
    public async Task InheritedEnvironmentWinsOverEveryFileLayer()
    {
        using var directory = new TemporaryDirectory();
        var workspace = directory.CreateDirectory("workspace");
        var dshHome = directory.CreateDirectory("dsh");
        await File.WriteAllTextAsync(Path.Combine(dshHome, ".credentials.yaml"), "DEEPSEEK_API_KEY: sk-managed");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".env"), "DEEPSEEK_API_KEY=sk-project");
        await File.WriteAllTextAsync(Path.Combine(dshHome, ".env"), "DEEPSEEK_API_KEY=sk-user");
        var provider = CreateProvider(workspace, dshHome, name =>
            name == "DEEPSEEK_API_KEY" ? " sk-inherited " : null);

        var result = await provider.GetCurrentAsync(CancellationToken.None);

        Assert.Equal("sk-inherited", result);
    }

    [Fact]
    public async Task ManagedCredentialWinsOverDotEnvLayersAndSupportsYamlQuotes()
    {
        using var directory = new TemporaryDirectory();
        var workspace = directory.CreateDirectory("workspace");
        var dshHome = directory.CreateDirectory("dsh");
        await File.WriteAllTextAsync(
            Path.Combine(dshHome, ".credentials.yaml"),
            "DEEPSEEK_API_KEY: 'sk-managed''quoted'");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".env"), "DEEPSEEK_API_KEY=sk-project");
        var provider = CreateProvider(workspace, dshHome);

        var result = await provider.GetCurrentAsync(CancellationToken.None);

        Assert.Equal("sk-managed'quoted", result);
    }

    [Fact]
    public async Task ProjectDotEnvWinsOverUserDotEnv()
    {
        using var directory = new TemporaryDirectory();
        var workspace = directory.CreateDirectory("workspace");
        var dshHome = directory.CreateDirectory("dsh");
        await File.WriteAllTextAsync(
            Path.Combine(workspace, ".env"),
            "export deepseek_api_key=\"sk-project\" # active workspace\n");
        await File.WriteAllTextAsync(Path.Combine(dshHome, ".env"), "DEEPSEEK_API_KEY=sk-user");
        var provider = CreateProvider(workspace, dshHome);

        var result = await provider.GetCurrentAsync(CancellationToken.None);

        Assert.Equal("sk-project", result);
    }

    [Fact]
    public async Task UserDotEnvIsUsedAsFinalFallback()
    {
        using var directory = new TemporaryDirectory();
        var workspace = directory.CreateDirectory("workspace");
        var dshHome = directory.CreateDirectory("dsh");
        await File.WriteAllTextAsync(Path.Combine(dshHome, ".env"), "DEEPSEEK_API_KEY=sk-user # fallback");
        var provider = CreateProvider(workspace, dshHome);

        var result = await provider.GetCurrentAsync(CancellationToken.None);

        Assert.Equal("sk-user", result);
    }

    [Fact]
    public async Task MissingOrMalformedSourcesReturnNullWithoutExposingAValue()
    {
        using var directory = new TemporaryDirectory();
        var workspace = directory.CreateDirectory("workspace");
        var dshHome = directory.CreateDirectory("dsh");
        await File.WriteAllTextAsync(Path.Combine(dshHome, ".credentials.yaml"), "DEEPSEEK_API_KEY: |\n  invalid\n");
        var provider = CreateProvider(workspace, dshHome);

        var result = await provider.GetCurrentAsync(CancellationToken.None);

        Assert.Null(result);
    }

    private static DeepSeekApiKeyProvider CreateProvider(
        string workspace,
        string dshHome,
        Func<string, string?>? getEnvironmentVariable = null) =>
        new(
            new AppSettings { WorkspacePath = workspace },
            getEnvironmentVariable ?? (_ => null),
            dshHome);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dsh-api-key-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
