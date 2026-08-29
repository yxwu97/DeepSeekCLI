using DeepSeekHarnessDesktop.Utilities;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.Services;

public sealed record CachedDshInstallation(
    string NodePath,
    string EntryPointPath,
    string Version);

public sealed class NpxDshCacheLocator
{
    private const int MaximumCandidateDirectories = 256;
    private const long MaximumManifestBytes = 64 * 1024;
    private readonly Func<string?> _cacheRootProvider;
    private readonly IDshTrustedVersionPolicy _trustedVersions;

    public NpxDshCacheLocator(
        Func<string?>? cacheRootProvider = null,
        IDshTrustedVersionPolicy? trustedVersions = null)
    {
        _cacheRootProvider = cacheRootProvider ?? DefaultCacheRoot;
        _trustedVersions = trustedVersions ?? new DshTrustedVersionPolicy();
    }

    public async Task<CachedDshInstallation?> FindAsync(
        string? nodePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nodePath) || !File.Exists(nodePath))
        {
            return null;
        }

        var cacheRoot = _cacheRootProvider();
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
        {
            return null;
        }

        foreach (var candidate in EnumerateCandidates(cacheRoot!))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installation = await ValidateCandidateAsync(
                candidate,
                nodePath!,
                _trustedVersions.Current.Version,
                cancellationToken);
            if (installation is not null)
            {
                return installation;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> EnumerateCandidates(string cacheRoot)
    {
        try
        {
            return Directory.GetDirectories(cacheRoot)
                .Select(TryGetCandidate)
                .Where(candidate => candidate is not null)
                .OrderByDescending(candidate => candidate!.LastWriteTimeUtc)
                .ThenBy(candidate => candidate!.Path, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumCandidateDirectories)
                .Select(candidate => candidate!.Path)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static CacheCandidate? TryGetCandidate(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0
                ? new CacheCandidate(path, Directory.GetLastWriteTimeUtc(path))
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<CachedDshInstallation?> ValidateCandidateAsync(
        string candidateRoot,
        string nodePath,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        var packageRoot = Path.Combine(candidateRoot, "node_modules", "@deepseek-ai", "dsh");
        var manifestPath = Path.Combine(packageRoot, "package.json");
        var entryPointPath = Path.Combine(packageRoot, "lib", "bin.js");
        try
        {
            if (!File.Exists(manifestPath)
                || !File.Exists(entryPointPath)
                || HasReparsePoint(
                    candidateRoot,
                    Path.Combine(candidateRoot, "node_modules"),
                    Path.Combine(candidateRoot, "node_modules", "@deepseek-ai"),
                    packageRoot,
                    Path.Combine(packageRoot, "lib"),
                    manifestPath,
                    entryPointPath)
                || new FileInfo(manifestPath).Length > MaximumManifestBytes)
            {
                return null;
            }

            using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return IsExpectedManifest(document.RootElement, expectedVersion)
                ? new CachedDshInstallation(nodePath, entryPointPath, expectedVersion)
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static bool HasReparsePoint(params string[] paths) => paths.Any(
        path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0);

    private static bool IsExpectedManifest(JsonElement root, string expectedVersion) =>
        root.TryGetProperty("name", out var name)
        && string.Equals(name.GetString(), DshPackageMetadata.PackageName, StringComparison.Ordinal)
        && root.TryGetProperty("version", out var version)
        && string.Equals(version.GetString(), expectedVersion, StringComparison.Ordinal)
        && root.TryGetProperty("bin", out var bin)
        && bin.ValueKind == JsonValueKind.Object
        && bin.TryGetProperty("dsh", out var entry)
        && string.Equals(entry.GetString(), "lib/bin.js", StringComparison.Ordinal);

    private static string? DefaultCacheRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "npm-cache", "_npx");
    }

    private sealed record CacheCandidate(string Path, DateTime LastWriteTimeUtc);
}
