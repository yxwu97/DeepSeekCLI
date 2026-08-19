using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DeepSeekHarnessDesktop.IntegrationTests;

internal sealed class FakeHarnessServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public FakeHarnessServer(Func<string, FakeResponse> handler)
    {
        Handler = handler;
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _loop = RunAsync();
    }

    public Uri BaseUri { get; }
    public Func<string, FakeResponse> Handler { get; set; }

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync().WaitAsync(_cts.Token);
                _ = RespondAsync(client);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task RespondAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, true, 1024, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync().WaitAsync(_cts.Token);
                var path = requestLine?.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "/";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync().WaitAsync(_cts.Token)))
                {
                }

                var response = Handler(path);
                if (response.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(response.Delay, _cts.Token);
                }
                var body = Encoding.UTF8.GetBytes(response.Body);
                var headers = new StringBuilder()
                    .Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(GetReason(response.StatusCode)).Append("\r\n")
                    .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
                    .Append("Content-Length: ").Append(body.Length).Append("\r\n")
                    .Append("Connection: close\r\n");
                if (response.Location is not null)
                {
                    headers.Append("Location: ").Append(response.Location).Append("\r\n");
                }
                headers.Append("\r\n");
                var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
                await stream.WriteAsync(headerBytes, 0, headerBytes.Length, _cts.Token);
                await stream.WriteAsync(body, 0, body.Length, _cts.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException)
            {
            }
        }
    }

    private static string GetReason(int statusCode) => statusCode switch
    {
        200 => "OK",
        302 => "Found",
        404 => "Not Found",
        503 => "Service Unavailable",
        _ => "Response",
    };

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        await _loop;
        _cts.Dispose();
    }
}

internal sealed record FakeResponse(
    int StatusCode = 200,
    string ContentType = "text/html; charset=utf-8",
    string Body = "<title>DeepSeek Harness</title><script>window.__DSH_BOOT__={};</script>",
    string? Location = null,
    TimeSpan Delay = default);
