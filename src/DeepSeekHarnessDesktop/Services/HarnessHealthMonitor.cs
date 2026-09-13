using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace DeepSeekHarnessDesktop.Services;

public sealed class HarnessHealthMonitor : IHarnessHealthMonitor, IDisposable
{
    public const int MaximumResponseBytes = 256 * 1024;
    public const int MaximumRedirects = 5;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly IDshBrowserSession? _browserSession;
    private Uri? _authenticatedOrigin;

    public HarnessHealthMonitor(IDshBrowserSession? browserSession = null)
    {
        _client = new HttpClient(CreateLoopbackHandler());
        _ownsClient = true;
        _browserSession = browserSession;
    }

    public static HttpClientHandler CreateLoopbackHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
    };

    public HarnessHealthMonitor(HttpClient client)
    {
        _client = client;
    }

    public async Task<HealthProbeResult> ProbeAsync(
        Uri uri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var requestedUri = uri;
        if (!ServiceUriValidator.TryNormalize(uri, out uri, out _))
        {
            return new HealthProbeResult(HealthProbeStatus.InvalidUri, requestedUri, Detail: "Only loopback HTTP(S) origins without user information, query, or fragment are allowed.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var current = uri;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (_browserSession?.GetAuthenticationUri(uri) is { } authenticationUri)
            {
                var authenticationOrigin = new UriBuilder(authenticationUri) { Query = string.Empty }.Uri;
                var result = await ProbeAuthenticatedAsync(authenticationOrigin, authenticationUri, timeout, timeoutCts.Token);
                return result with { RequestedUri = uri };
            }
            for (var redirects = 0; ; redirects++)
            {
                if (!visited.Add(current.AbsoluteUri))
                {
                    return new HealthProbeResult(HealthProbeStatus.InvalidUri, uri, current, "Redirect loop detected.");
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                using var response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirects >= MaximumRedirects || response.Headers.Location is null)
                    {
                        return new HealthProbeResult(HealthProbeStatus.InvalidUri, uri, current, "Redirect limit exceeded or Location is missing.");
                    }

                    Uri next;
                    try
                    {
                        next = response.Headers.Location.IsAbsoluteUri
                            ? response.Headers.Location
                            : new Uri(current, response.Headers.Location);
                    }
                    catch (UriFormatException exception)
                    {
                        return new HealthProbeResult(HealthProbeStatus.InvalidUri, uri, current, exception.Message);
                    }

                    if (!ServiceUriValidator.IsAllowedLoopbackTarget(next)
                        || (_authenticatedOrigin is not null
                            && !CodeWebViewService.IsSameOrigin(next, _authenticatedOrigin)))
                    {
                        return new HealthProbeResult(HealthProbeStatus.ExternalRedirect, uri, next, "Redirect target is outside loopback HTTP(S).");
                    }

                    current = next;
                    continue;
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return new HealthProbeResult(HealthProbeStatus.AuthenticationRequired, uri, current,
                        "Loopback service requires authentication; identity is unconfirmed.", response.StatusCode);
                }

                if (!response.IsSuccessStatusCode || !IsHtml(response.Content.Headers.ContentType))
                {
                    return new HealthProbeResult(
                        HealthProbeStatus.ReachableUnknown,
                        uri,
                        current,
                        $"HTTP {(int)response.StatusCode} or non-HTML response.",
                        response.StatusCode);
                }

                var body = await ReadBoundedBodyAsync(response.Content, timeoutCts.Token);
                if (body is null)
                {
                    return new HealthProbeResult(HealthProbeStatus.ReachableUnknown, uri, current, "HTML response exceeds 256 KiB.");
                }

                var hasBootMarker = body.Contains("window.__DSH_BOOT__", StringComparison.Ordinal)
                    || body.Contains("globalThis[\"__DSH_BOOT__\"]", StringComparison.Ordinal);
                var confirmed = body.Contains("<title>DeepSeek Harness</title>", StringComparison.Ordinal)
                    && hasBootMarker;
                return new HealthProbeResult(
                    confirmed ? HealthProbeStatus.DshConfirmed : HealthProbeStatus.ReachableUnknown,
                    uri,
                    current,
                    confirmed ? null : "DSH identity features are missing.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthProbeResult(HealthProbeStatus.Unreachable, uri, Detail: "Probe timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new HealthProbeResult(HealthProbeStatus.Unreachable, uri, Detail: $"HTTP probe failed: {exception.GetType().Name}.");
        }
        catch (IOException exception)
        {
            return new HealthProbeResult(HealthProbeStatus.Unreachable, uri, Detail: exception.Message);
        }
    }

    public async Task<HealthProbeResult> WaitUntilReadyAsync(
        Func<Uri> uriProvider,
        TimeSpan startupTimeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var first = true;
        HealthProbeResult? last = null;
        while (stopwatch.Elapsed < startupTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = startupTimeout - stopwatch.Elapsed;
            var timeout = remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2);
            if (timeout <= TimeSpan.Zero)
            {
                break;
            }

            last = await ProbeAsync(uriProvider(), timeout, cancellationToken);
            if (last.Status != HealthProbeStatus.Unreachable
                && last.Status != HealthProbeStatus.AuthenticationRequired
                && !(last.Status == HealthProbeStatus.ReachableUnknown
                    && last.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized))
            {
                return last;
            }

            var delay = first ? TimeSpan.FromMilliseconds(300) : TimeSpan.FromMilliseconds(500);
            first = false;
            remaining = startupTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }
            await Task.Delay(delay < remaining ? delay : remaining, cancellationToken);
        }

        var requested = last?.RequestedUri ?? uriProvider();
        if (last?.Status is HealthProbeStatus.ReachableUnknown or HealthProbeStatus.AuthenticationRequired) return last;
        return new HealthProbeResult(HealthProbeStatus.Unreachable, requested, Detail: "Startup timeout elapsed.");
    }

    private static async Task<HealthProbeResult> ProbeAuthenticatedAsync(
        Uri origin, Uri authenticationUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // The HTTP probe owns a short-lived cookie jar; Code WebView2 performs its own exchange.
        using var handler = CreateLoopbackHandler();
        handler.UseCookies = true;
        handler.CookieContainer = new CookieContainer();
        using var client = new HttpClient(handler);
        using (var response = await client.GetAsync(authenticationUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            var location = response.Headers.Location;
            if (response.StatusCode != HttpStatusCode.SeeOther || location is null
                || !Uri.TryCreate(origin, location, out var target)
                || target != origin)
            {
                return new HealthProbeResult(
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? HealthProbeStatus.AuthenticationRequired : HealthProbeStatus.ReachableUnknown, origin, origin,
                    "DSH browser authentication did not return the clean same-origin root.", response.StatusCode);
            }
        }
        using var monitor = new HarnessHealthMonitor(client) { _authenticatedOrigin = origin };
        return await monitor.ProbeAsync(origin, timeout, cancellationToken);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        (HttpStatusCode)308;

    private static bool IsHtml(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.Equals("text/html", StringComparison.OrdinalIgnoreCase) == true
        || contentType?.MediaType?.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<string?> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = await content.ReadAsStreamAsync();
        using var memory = new MemoryStream(MaximumResponseBytes + 1);
        var buffer = new byte[16 * 1024];
        while (memory.Length <= MaximumResponseBytes)
        {
            var count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (count == 0)
            {
                return Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
            }
            await memory.WriteAsync(buffer, 0, count, cancellationToken);
        }
        return null;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
