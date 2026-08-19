using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using NuGet.Versioning;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshReleaseService : IDshReleaseService, IDisposable
{
    public const int MaximumResponseBytes = 64 * 1024;
    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly bool _ownsClient;

    public DshReleaseService()
        : this(new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        }), DshPackageMetadata.NpmLatestUri, ownsClient: true)
    {
    }

    public DshReleaseService(HttpClient client)
        : this(client, DshPackageMetadata.NpmLatestUri, ownsClient: false)
    {
    }

    internal DshReleaseService(HttpClient client, Uri endpoint)
        : this(client, endpoint, ownsClient: false)
    {
    }

    private DshReleaseService(HttpClient client, Uri endpoint, bool ownsClient)
    {
        _client = client;
        _endpoint = endpoint;
        _ownsClient = ownsClient;
    }

    public async Task<DshUpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.Now;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var json = await ReadBoundedAsync(response.Content, timeout.Token);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("version", out var element)
                || element.ValueKind != JsonValueKind.String
                || !NuGetVersion.TryParse(element.GetString(), out var latest))
            {
                throw new InvalidDataException("npm 响应未包含有效的 version 字段。");
            }

            return new DshUpdateCheckResult(
                latest.ToNormalizedString(),
                checkedAt);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(checkedAt, "检查更新超时，请稍后重试。");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException)
        {
            return Failure(checkedAt, $"检查更新失败：{exception.Message}");
        }
    }

    private static DshUpdateCheckResult Failure(DateTimeOffset checkedAt, string message) =>
        new(null, checkedAt, message);

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = await content.ReadAsStreamAsync();
        using var memory = new MemoryStream(MaximumResponseBytes + 1);
        var buffer = new byte[8192];
        while (memory.Length <= MaximumResponseBytes)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read == 0)
            {
                return memory.ToArray();
            }

            await memory.WriteAsync(buffer, 0, read, cancellationToken);
        }

        throw new InvalidDataException("npm 响应超过 64 KiB 限制。");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
