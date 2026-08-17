namespace DeepSeekHarnessDesktop.Utilities;

public static class ServiceUriValidator
{
    public static bool TryNormalize(string? value, out Uri normalized, out string error)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate))
        {
            normalized = DshPackageMetadata.DefaultServiceUri;
            error = "请输入完整的本机 HTTP 或 HTTPS 地址。";
            return false;
        }

        return TryNormalize(candidate, out normalized, out error);
    }

    public static bool TryNormalize(Uri? candidate, out Uri normalized, out string error)
    {
        normalized = DshPackageMetadata.DefaultServiceUri;
        if (candidate is null
            || !candidate.IsAbsoluteUri
            || candidate.Scheme is not ("http" or "https")
            || !candidate.IsLoopback)
        {
            error = "服务地址必须是本机 loopback HTTP 或 HTTPS 地址。";
            return false;
        }

        if (!string.IsNullOrEmpty(candidate.UserInfo))
        {
            error = "服务地址不能包含用户名或密码。";
            return false;
        }

        if (!string.IsNullOrEmpty(candidate.Query) || !string.IsNullOrEmpty(candidate.Fragment))
        {
            error = "服务地址不能包含查询参数或片段。";
            return false;
        }

        if (candidate.Port is < 1 or > 65535)
        {
            error = "服务端口必须介于 1 和 65535 之间。";
            return false;
        }

        normalized = new UriBuilder(candidate)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri;
        error = string.Empty;
        return true;
    }

    public static Uri NormalizeOrThrow(Uri candidate)
    {
        if (TryNormalize(candidate, out var normalized, out var error))
        {
            return normalized;
        }

        throw new InvalidDataException(error);
    }

    public static bool IsAllowed(Uri? candidate) => TryNormalize(candidate, out _, out _);

    public static bool IsAllowedLoopbackTarget(Uri? candidate) => candidate is not null
        && candidate.IsAbsoluteUri
        && candidate.IsLoopback
        && candidate.Scheme is "http" or "https"
        && string.IsNullOrEmpty(candidate.UserInfo)
        && candidate.Port is >= 1 and <= 65535;
}
