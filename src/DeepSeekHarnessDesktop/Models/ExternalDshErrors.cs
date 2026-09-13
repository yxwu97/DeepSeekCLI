namespace DeepSeekHarnessDesktop.Models;

public static class ExternalDshErrors
{
    public static HarnessError AuthenticationRequired() => new(
        "DSH-E228", "本机服务需要认证，请使用 DSH 终端中的认证链接连接",
        "Loopback service requires authentication; DSH identity has not been confirmed.", true);

    public static HarnessError InvalidLink() => new(
        "DSH-E229", "认证链接无效，请粘贴当前服务地址对应的完整 DSH 认证链接",
        "External authentication URL rejected by the fixed same-origin URL policy.", true);
}
