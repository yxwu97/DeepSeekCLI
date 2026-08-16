using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DeepSeekAccountService(HttpClient httpClient) : IDeepSeekAccountService
{
    public static readonly Uri BalanceEndpoint = new("https://api.deepseek.com/user/balance");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    public async Task<DeepSeekAccountSnapshot> GetBalanceAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        var normalizedApiKey = apiKey.Trim();
        if (normalizedApiKey.Length == 0)
        {
            throw CreateException(
                "API-E600",
                "请输入 DeepSeek API Key",
                "A DeepSeek API key is required.",
                false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BalanceEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedApiKey);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw MapStatusCode(response.StatusCode);
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<BalanceResponse>(
                content,
                JsonOptions,
                cancellationToken);
            if (payload?.IsAvailable is null || payload.BalanceInfos is null)
            {
                throw CreateInvalidResponseException();
            }

            var balances = payload.BalanceInfos.Select(MapBalance).ToArray();
            return new DeepSeekAccountSnapshot(payload.IsAvailable.Value, balances);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateException(
                "API-E603",
                "DeepSeek API 请求超时",
                "The DeepSeek balance request timed out.",
                true);
        }
        catch (HttpRequestException exception)
        {
            throw CreateException(
                "API-E604",
                "DeepSeek API 暂时不可用",
                "The DeepSeek balance endpoint could not be reached.",
                true,
                exception);
        }
        catch (JsonException exception)
        {
            throw CreateInvalidResponseException(exception);
        }
        catch (FormatException exception)
        {
            throw CreateInvalidResponseException(exception);
        }
    }

    private static DeepSeekBalanceInfo MapBalance(BalanceInfoResponse balance)
    {
        if (string.IsNullOrWhiteSpace(balance.Currency))
        {
            throw new FormatException("A balance currency is required.");
        }

        return new DeepSeekBalanceInfo(
            balance.Currency.Trim().ToUpperInvariant(),
            ParseAmount(balance.TotalBalance),
            ParseAmount(balance.GrantedBalance),
            ParseAmount(balance.ToppedUpBalance));
    }

    private static decimal ParseAmount(string? value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            throw new FormatException("A balance amount was not a valid decimal value.");
        }
        return amount;
    }

    private static DeepSeekAccountException MapStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => CreateException(
            "API-E601",
            "API Key 无效或无权访问账户信息",
            $"DeepSeek balance request was rejected with HTTP {(int)statusCode}.",
            false),
        HttpStatusCode.TooManyRequests => CreateException(
            "API-E602",
            "请求过于频繁，请稍后重试",
            "DeepSeek balance request was rate limited.",
            true),
        >= HttpStatusCode.InternalServerError => CreateException(
            "API-E604",
            "DeepSeek API 暂时不可用",
            $"DeepSeek balance endpoint returned HTTP {(int)statusCode}.",
            true),
        _ => CreateException(
            "API-E606",
            "无法查询 DeepSeek 账户信息",
            $"DeepSeek balance endpoint returned HTTP {(int)statusCode}.",
            true),
    };

    private static DeepSeekAccountException CreateInvalidResponseException(Exception? exception = null) =>
        CreateException(
            "API-E605",
            "DeepSeek API 返回了无法识别的数据",
            "DeepSeek balance response did not match the documented schema.",
            true,
            exception);

    private static DeepSeekAccountException CreateException(
        string code,
        string userMessage,
        string technicalMessage,
        bool retryable,
        Exception? exception = null) =>
        new(new DeepSeekAccountError(code, userMessage, technicalMessage, retryable, exception));

    private sealed record BalanceResponse(
        [property: JsonPropertyName("is_available")] bool? IsAvailable,
        [property: JsonPropertyName("balance_infos")] BalanceInfoResponse[]? BalanceInfos);

    private sealed record BalanceInfoResponse(
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("total_balance")] string? TotalBalance,
        [property: JsonPropertyName("granted_balance")] string? GrantedBalance,
        [property: JsonPropertyName("topped_up_balance")] string? ToppedUpBalance);
}
