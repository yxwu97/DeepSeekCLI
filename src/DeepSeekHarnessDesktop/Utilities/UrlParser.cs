using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Utilities;

public static partial class UrlParser
{
    public static Uri? TryParseLoopback(string? outputLine)
    {
        var line = OutputLineProcessor.Normalize(outputLine);
        if (line is null)
        {
            return null;
        }

        var match = LoopbackUrlRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var text = TrimTrailingPunctuation(match.Value);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !uri.IsLoopback
            || uri.Scheme is not ("http" or "https")
            || uri.Port is < 1 or > 65535)
        {
            return null;
        }

        return uri;
    }

    private static string TrimTrailingPunctuation(string value)
    {
        value = value.TrimEnd('.', ',', ';');
        while (value.EndsWith(')') && value.Count(character => character == ')') > value.Count(character => character == '('))
        {
            value = value[..^1];
        }

        return value;
    }

    [GeneratedRegex(@"https?://(?:127\.0\.0\.1|localhost|\[::1\])(?::\d{1,5})?/?[^\s\x1B]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoopbackUrlRegex();
}
