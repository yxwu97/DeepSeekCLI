using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCatalogClient : IDshCatalogService, IDisposable
{
    private const int MaximumSignatureBytes = 1024;
    private const long MaximumStateBytes = 32 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly HttpClient _client;
    private readonly DshCatalogVerifier _verifier;
    private readonly Uri _catalogEndpoint;
    private readonly Uri _signatureEndpoint;
    private readonly Func<string> _stateRootProvider;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DshCatalogClient(
        DshCatalogVerifier verifier,
        Uri catalogEndpoint,
        Uri signatureEndpoint,
        Func<string>? stateRootProvider = null)
        : this(
            new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
            }),
            verifier,
            catalogEndpoint,
            signatureEndpoint,
            stateRootProvider,
            ownsClient: true)
    {
    }

    public DshCatalogClient(
        HttpClient client,
        DshCatalogVerifier verifier,
        Uri catalogEndpoint,
        Uri signatureEndpoint,
        Func<string>? stateRootProvider = null)
        : this(client, verifier, catalogEndpoint, signatureEndpoint, stateRootProvider, ownsClient: false)
    {
    }

    private DshCatalogClient(
        HttpClient client,
        DshCatalogVerifier verifier,
        Uri catalogEndpoint,
        Uri signatureEndpoint,
        Func<string>? stateRootProvider,
        bool ownsClient)
    {
        ValidateEndpoint(catalogEndpoint);
        ValidateEndpoint(signatureEndpoint);
        if (!string.Equals(catalogEndpoint.Host, signatureEndpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Catalog and signature endpoints must use the same fixed host.");
        }
        _client = client;
        _verifier = verifier;
        _catalogEndpoint = catalogEndpoint;
        _signatureEndpoint = signatureEndpoint;
        _stateRootProvider = stateRootProvider ?? DefaultStateRoot;
        _ownsClient = ownsClient;
    }

    public async Task<DshCatalogLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.Now;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var catalogTask = DownloadBoundedAsync(
                    _catalogEndpoint,
                    DshCatalogVerifier.MaximumCatalogBytes,
                    cancellationToken);
                var signatureTask = DownloadBoundedAsync(
                    _signatureEndpoint,
                    MaximumSignatureBytes,
                    cancellationToken);
                await Task.WhenAll(catalogTask, signatureTask);
                var catalog = await catalogTask;
                var signature = await signatureTask;
                var snapshot = _verifier.VerifyAndParse(catalog, signature);
                await AcceptFreshAsync(snapshot, catalog, signature, cancellationToken);
                return new DshCatalogLoadResult(snapshot, false, checkedAt);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return await LoadCachedOrFailureAsync(
                    checkedAt,
                    "The signed DSH catalog request timed out.",
                    cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                return await LoadCachedOrFailureAsync(
                    checkedAt,
                    $"The signed DSH catalog could not be downloaded: {exception.Message}",
                    cancellationToken);
            }
            catch (HarnessException exception)
            {
                return new DshCatalogLoadResult(null, false, checkedAt, exception.Error);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException
                    or CryptographicException or ArgumentException)
            {
                return new DshCatalogLoadResult(
                    null,
                    false,
                    checkedAt,
                    DshUpdateErrorMapper.CatalogRejected(
                        "The signed DSH catalog state could not be processed.",
                        exception));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AcceptFreshAsync(
        DshCatalogSnapshot snapshot,
        byte[] catalog,
        byte[] signature,
        CancellationToken cancellationToken)
    {
        var digest = ComputeSha256(catalog);
        var root = GetControlledRoot();
        var accepted = await ReadAcceptedStatesAsync(root, cancellationToken);
        var highest = accepted.OrderByDescending(state => state.CatalogSequence).FirstOrDefault();
        if (highest is not null
            && (snapshot.CatalogSequence < highest.CatalogSequence
                || (snapshot.CatalogSequence == highest.CatalogSequence
                    && !string.Equals(digest, highest.CatalogSha256, StringComparison.OrdinalIgnoreCase))))
        {
            throw new HarnessException(DshUpdateErrorMapper.CatalogRejected(
                $"Catalog replay or sequence collision rejected. Received {snapshot.CatalogSequence}; accepted {highest.CatalogSequence}."));
        }

        var cacheName = $"{snapshot.CatalogSequence}-{digest.Substring(0, 16)}";
        var cacheRoot = Path.Combine(root, "cache");
        EnsureControlledDirectory(cacheRoot);
        var cachePath = Path.Combine(cacheRoot, cacheName);
        EnsureDirectChild(cacheRoot, cachePath);
        if (!Directory.Exists(cachePath))
        {
            Directory.CreateDirectory(cachePath);
            try
            {
                await WriteBytesAsync(Path.Combine(cachePath, "catalog.json"), catalog, cancellationToken);
                await WriteBytesAsync(Path.Combine(cachePath, "catalog.sig"), signature, cancellationToken);
            }
            catch
            {
                DeleteCacheDirectory(cacheRoot, cachePath);
                throw;
            }
        }
        else
        {
            EnsureControlledDirectory(cachePath);
            var cachedCatalog = await ReadFileBoundedAsync(
                Path.Combine(cachePath, "catalog.json"),
                DshCatalogVerifier.MaximumCatalogBytes,
                cancellationToken);
            if (!string.Equals(ComputeSha256(cachedCatalog), digest, StringComparison.OrdinalIgnoreCase))
            {
                throw new HarnessException(DshUpdateErrorMapper.CatalogRejected(
                    "An existing catalog cache directory does not match its accepted digest."));
            }
        }

        await WriteAcceptedStateAsync(
            root,
            new AcceptedCatalogState(snapshot.CatalogSequence, digest, cacheName),
            cancellationToken);
    }

    private async Task<DshCatalogLoadResult> LoadCachedOrFailureAsync(
        DateTimeOffset checkedAt,
        string networkError,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = GetControlledRoot(create: false);
            if (!Directory.Exists(root))
            {
                return Failure(checkedAt, networkError);
            }
            var states = await ReadAcceptedStatesAsync(root, cancellationToken);
            foreach (var state in states.OrderByDescending(value => value.CatalogSequence))
            {
                var cacheRoot = Path.Combine(root, "cache");
                var cachePath = Path.Combine(cacheRoot, state.CacheDirectory);
                try
                {
                    EnsureDirectChild(cacheRoot, cachePath);
                    if (!Directory.Exists(cachePath) || IsReparsePoint(cachePath))
                    {
                        continue;
                    }
                    var catalog = await ReadFileBoundedAsync(
                        Path.Combine(cachePath, "catalog.json"),
                        DshCatalogVerifier.MaximumCatalogBytes,
                        cancellationToken);
                    var signature = await ReadFileBoundedAsync(
                        Path.Combine(cachePath, "catalog.sig"),
                        MaximumSignatureBytes,
                        cancellationToken);
                    var snapshot = _verifier.VerifyAndParse(catalog, signature);
                    if (snapshot.CatalogSequence == state.CatalogSequence
                        && string.Equals(
                            ComputeSha256(catalog),
                            state.CatalogSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return new DshCatalogLoadResult(snapshot, true, checkedAt);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or HarnessException
                        or JsonException or ArgumentException)
                {
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new DshCatalogLoadResult(
                null,
                false,
                checkedAt,
                DshUpdateErrorMapper.CatalogRejected(
                    "The cached signed DSH catalog state is invalid.",
                    exception));
        }
        return Failure(checkedAt, networkError);
    }

    private async Task<byte[]> DownloadBoundedAsync(
        Uri endpoint,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest
            || response.RequestMessage?.RequestUri is not { } finalUri
            || !Uri.Equals(finalUri, endpoint))
        {
            throw new HarnessException(DshUpdateErrorMapper.CatalogRejected(
                "The signed DSH catalog endpoint attempted a redirect."));
        }
        response.EnsureSuccessStatusCode();
        return await ReadStreamBoundedAsync(
            await response.Content.ReadAsStreamAsync(),
            maximumBytes,
            timeout.Token);
    }

    private static async Task<byte[]> ReadStreamBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using (stream)
        using (var memory = new MemoryStream(maximumBytes + 1))
        {
            var buffer = new byte[8192];
            while (memory.Length <= maximumBytes)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read == 0)
                {
                    return memory.ToArray();
                }
                await memory.WriteAsync(buffer, 0, read, cancellationToken);
            }
        }
        throw new HarnessException(DshUpdateErrorMapper.CatalogRejected(
            $"A signed DSH catalog response exceeded {maximumBytes} bytes."));
    }

    private static async Task<byte[]> ReadFileBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || IsReparsePoint(path) || new FileInfo(path).Length > maximumBytes)
        {
            throw new InvalidDataException("A signed DSH catalog cache file is missing or oversized.");
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        return await ReadStreamBoundedAsync(stream, maximumBytes, cancellationToken);
    }

    private static async Task WriteBytesAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);
        await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AcceptedCatalogState>> ReadAcceptedStatesAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var states = new List<AcceptedCatalogState>();
        foreach (var path in new[] { StatePath(root), StateBackupPath(root) })
        {
            try
            {
                if (!File.Exists(path) || IsReparsePoint(path) || new FileInfo(path).Length > MaximumStateBytes)
                {
                    continue;
                }
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                var state = await JsonSerializer.DeserializeAsync<AcceptedCatalogState>(
                    stream,
                    cancellationToken: cancellationToken);
                if (state is not null && IsValidState(state))
                {
                    states.Add(state);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
        return states;
    }

    private static async Task WriteAcceptedStateAsync(
        string root,
        AcceptedCatalogState state,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(root, $"accepted-catalog.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                true))
            {
                await JsonSerializer.SerializeAsync(stream, state, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(StatePath(root)))
            {
                File.Replace(temporaryPath, StatePath(root), StateBackupPath(root), true);
            }
            else
            {
                File.Move(temporaryPath, StatePath(root));
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetControlledRoot(bool create = true)
    {
        var root = Path.GetFullPath(_stateRootProvider());
        if (create)
        {
            EnsureControlledDirectory(root);
        }
        else if (Directory.Exists(root) && IsReparsePoint(root))
        {
            throw new InvalidDataException("The signed DSH catalog state root is a reparse point.");
        }
        return root;
    }

    private static void EnsureControlledDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (IsReparsePoint(path))
        {
            throw new InvalidDataException($"Controlled catalog directory is a reparse point: {path}");
        }
    }

    private static void EnsureDirectChild(string parent, string child)
    {
        var expected = PathCompatibility.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var actual = PathCompatibility.TrimEndingDirectorySeparator(
            Path.GetDirectoryName(Path.GetFullPath(child))
                ?? throw new ArgumentException("Catalog path has no parent."));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Catalog path is outside its controlled parent.");
        }
    }

    private static void DeleteCacheDirectory(string parent, string path)
    {
        EnsureDirectChild(parent, path);
        if (Directory.Exists(path) && !IsReparsePoint(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static bool IsValidState(AcceptedCatalogState state) =>
        state.CatalogSequence > 0
        && state.CatalogSha256.Length == 64
        && state.CatalogSha256.All(Uri.IsHexDigit)
        && string.Equals(
            state.CacheDirectory,
            $"{state.CatalogSequence}-{state.CatalogSha256.Substring(0, 16)}",
            StringComparison.Ordinal);

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("A fixed HTTPS catalog endpoint is required.");
        }
    }

    private static DshCatalogLoadResult Failure(DateTimeOffset checkedAt, string technical) =>
        new(null, false, checkedAt, DshUpdateErrorMapper.CatalogRejected(technical));

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string StatePath(string root) => Path.Combine(root, "accepted-catalog.json");
    private static string StateBackupPath(string root) => Path.Combine(root, "accepted-catalog.backup.json");
    private static string DefaultStateRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepSeekHarnessDesktop",
        "dsh-runtime",
        "catalog");

    public void Dispose()
    {
        _gate.Dispose();
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private sealed record AcceptedCatalogState(
        long CatalogSequence,
        string CatalogSha256,
        string CacheDirectory);
}
