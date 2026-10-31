using DeepSeekHarnessDesktop.Models;
using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Utilities;

public static class NpmFailureClassifier
{
    public static HarnessError? Classify(IEnumerable<string> standardError)
    {
        var text = string.Join("\n", standardError);
        if (text.Length == 0)
        {
            return null;
        }

        // npm cleanup warnings can contain EPERM after a network failure.
        // Prefer the primary error code rather than the cleanup side effect.
        var codes = Regex.Matches(text, @"(?im)^npm\s+(?:error|ERR!)\s+code\s+([A-Z][A-Z0-9_]*)\b")
            .Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
        if (codes.Length > 0) text = string.Join("\n", codes);

        if (ContainsAny(text, "ENOTFOUND", "EAI_AGAIN", "getaddrinfo", "EIDLETIMEOUT",
            "ETIMEDOUT", "ESOCKETTIMEDOUT", "ECONNRESET", "ECONNREFUSED", "EPIPE"))
        {
            return Error("DSH-E211", "npm 下载连接中断或超时，请检查网络后重试，已下载缓存会保留",
                "npm registry connection failed or timed out.");
        }
        if (ContainsAny(text, "CERT_", "certificate", "SELF_SIGNED_CERT", "UNABLE_TO_VERIFY_LEAF_SIGNATURE"))
        {
            return Error("DSH-E212", "npm 安全连接失败，请检查系统时间、代理和证书", "npm TLS validation failed.");
        }
        if (ContainsAny(text, "EACCES", "EPERM", "permission denied", "access is denied"))
        {
            return Error("DSH-E214", "npm 缓存或目录权限不足，请检查当前用户权限", "npm reported a filesystem permission error.");
        }
        if (ContainsAny(text, "E401", "E403", "E404", "registry returned", "registry.npmjs.org"))
        {
            return Error("DSH-E213", "npm registry 拒绝或未找到 DSH 包，请检查 registry 配置", "npm registry request failed.");
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static HarnessError Error(string code, string userMessage, string technicalMessage) =>
        new(code, userMessage, technicalMessage, true);
}
