namespace DeepSeekHarnessDesktop.Utilities;

public enum ChatNavigationDecision
{
    Embed,
    OpenExternal,
    Reject,
}

public static class ChatNavigationPolicy
{
    public const string ChatHost = "chat.deepseek.com";

    public static readonly Uri EntryUri = new("https://chat.deepseek.com/");

    public static ChatNavigationDecision Decide(Uri? target)
    {
        if (target is null || !target.IsAbsoluteUri || target.UserInfo.Length != 0)
        {
            return ChatNavigationDecision.Reject;
        }

        var isHttp = target.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        var isHttps = target.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (!isHttp && !isHttps)
        {
            return ChatNavigationDecision.Reject;
        }

        if (target.Host.EndsWith(".", StringComparison.Ordinal) || !target.IsDefaultPort)
        {
            return ChatNavigationDecision.Reject;
        }

        var isChatHost = target.Host.Equals(ChatHost, StringComparison.OrdinalIgnoreCase)
            && target.IdnHost.Equals(ChatHost, StringComparison.OrdinalIgnoreCase);
        if (isChatHost)
        {
            return isHttps ? ChatNavigationDecision.Embed : ChatNavigationDecision.Reject;
        }

        return ChatNavigationDecision.OpenExternal;
    }
}
