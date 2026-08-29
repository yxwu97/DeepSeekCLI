using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Net;
using System.Security.Cryptography;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCatalogAssetDownloader : IDshCatalogAssetDownloader, IDisposable
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(2);
    private readonly HttpClient _client;
    private readonly string _assetHost;
    private readonly Func<string> _rootProvider;
    private readonly bool _ownsClient;

    public DshCatalogAssetDownloader(string assetHost, Func<string>? rootProvider = null)
        : this(
            new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
            }),
            assetHost,
            rootProvider,
            ownsClient: true)
    {
    }

    public DshCatalogAssetDownloader(
        HttpClient client,
        string assetHost,
        Func<string>? rootProvider = null)
        : this(client, assetHost, rootProvider, ownsClient: false)
    {
    }

    private DshCatalogAssetDownloader(
        HttpClient client,
        string assetHost,
        Func<string>? rootProvider,
        bool ownsClient)
    {
        _client = client;
        _assetHost = assetHost;
        _rootProvider = rootProvider ?? DefaultRoot;
        _ownsClient = ownsClient;
    }

    public async Task<DshDownloadedAssets> DownloadAsync(
        DshCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(_rootProvider());
        EnsureControlledDirectory(root);
        var transactionRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
        EnsureDirectChild(root, transactionRoot);
        Directory.CreateDirectory(transactionRoot);
        try
        {
            var packagePath = Path.Combine(transactionRoot, "package.json");
            var lockPath = Path.Combine(transactionRoot, "package-lock.json");
            await DownloadAssetAsync(entry.Package, packagePath, cancellationToken);
            await DownloadAssetAsync(entry.Lock, lockPath, cancellationToken);
            return new DshDownloadedAssets(transactionRoot, packagePath, lockPath);
        }
        catch
        {
            DeleteControlledTree(root, transactionRoot);
            throw;
        }
    }

    public Task CleanupAsync(DshDownloadedAssets assets)
    {
        var root = Path.GetFullPath(_rootProvider());
        DeleteControlledTree(root, assets.RootPath);
        return Task.CompletedTask;
    }

    private async Task DownloadAssetAsync(
        DshCatalogAsset asset,
        string destination,
        CancellationToken cancellationToken)
    {
        ValidateAssetUri(asset.Url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest
                || response.RequestMessage?.RequestUri is not { } finalUri
                || !Uri.Equals(finalUri, asset.Url))
            {
                throw Rejected("A signed DSH asset endpoint attempted a redirect.");
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } contentLength
                && contentLength != asset.Bytes)
            {
                throw Rejected("A signed DSH asset Content-Length does not match the catalog.");
            }

            using var input = await response.Content.ReadAsStreamAsync();
            using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                true);
            using var sha = SHA256.Create();
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
            {
                total += read;
                if (total > asset.Bytes)
                {
                    throw Rejected("A signed DSH asset exceeded its catalog byte length.");
                }
                sha.TransformBlock(buffer, 0, read, null, 0);
                await output.WriteAsync(buffer, 0, read, timeout.Token);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            await output.FlushAsync(timeout.Token);
            var digest = BitConverter.ToString(sha.Hash!).Replace("-", string.Empty).ToLowerInvariant();
            if (total != asset.Bytes
                || !string.Equals(digest, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Rejected("A signed DSH asset byte length or SHA-256 does not match the catalog.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Rejected("A signed DSH asset download timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw Rejected("A signed DSH asset could not be downloaded.", exception);
        }
    }

    private void ValidateAssetUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, _assetHost, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Rejected("A signed DSH asset URL is outside the fixed HTTPS host.");
        }
    }

    private static void EnsureControlledDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (IsReparsePoint(path))
        {
            throw Rejected("The controlled DSH asset directory is a reparse point.");
        }
    }

    private static void EnsureDirectChild(string parent, string child)
    {
        var expected = PathCompatibility.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var actual = PathCompatibility.TrimEndingDirectorySeparator(
            Path.GetDirectoryName(Path.GetFullPath(child))
                ?? throw new ArgumentException("DSH asset path has no parent."));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("DSH asset path is outside its controlled parent.");
        }
    }

    private static void DeleteControlledTree(string parent, string path)
    {
        EnsureDirectChild(parent, path);
        if (!Directory.Exists(path))
        {
            return;
        }
        if (IsReparsePoint(path))
        {
            throw Rejected("The controlled DSH asset transaction became a reparse point.");
        }
        Directory.Delete(path, true);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static HarnessException Rejected(string technical, Exception? exception = null) =>
        new(DshUpdateErrorMapper.CatalogRejected(technical, exception));

    private static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepSeekHarnessDesktop",
        "dsh-runtime",
        "downloads");

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
