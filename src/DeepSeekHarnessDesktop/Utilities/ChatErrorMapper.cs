using DeepSeekHarnessDesktop.Models;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekHarnessDesktop.Utilities;

public static class ChatErrorMapper
{
    public static HarnessError NavigationFailure(CoreWebView2WebErrorStatus status, int httpStatusCode)
    {
        if (httpStatusCode >= 400)
        {
            return new HarnessError("WEB-E314", "DeepSeek Chat 服务返回错误", $"HTTP status {httpStatusCode}.", true);
        }

        return status switch
        {
            CoreWebView2WebErrorStatus.HostNameNotResolved =>
                new("WEB-E312", "无法解析 DeepSeek Chat 地址", "Chat host name was not resolved.", true),
            CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect
                or CoreWebView2WebErrorStatus.CertificateExpired
                or CoreWebView2WebErrorStatus.ClientCertificateContainsErrors
                or CoreWebView2WebErrorStatus.CertificateRevoked
                or CoreWebView2WebErrorStatus.CertificateIsInvalid =>
                new("WEB-E313", "DeepSeek Chat 安全连接失败", $"TLS failure: {status}.", true),
            _ => new("WEB-E312", "无法连接 DeepSeek Chat", $"Navigation failure: {status}.", true),
        };
    }
}
