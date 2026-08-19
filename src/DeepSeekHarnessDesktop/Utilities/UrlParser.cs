using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Utilities;

public static class UrlParser
{
    private static readonly Regex LoopbackUrlPattern = new(
        @"https?://(?:127\.0\.0\.1|localhost|\[::1\])(?::\d{1,5})?/?[^\s\x1B]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static Uri? TryParseLoopback(string? outputLine)
    {
        var line = OutputLineProcessor.Normalize(outputLine);
        if (line is null)
        {
            return null;
        }

        var match = LoopbackUrlPattern.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var text = TrimTrailingPunctuation(match.Value);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !ServiceUriValidator.TryNormalize(uri, out var normalized, out _))
        {
            return null;
        }

        return normalized;
    }

    private static string TrimTrailingPunctuation(string value)
    {
        value = value.TrimEnd('.', ',', ';');
        while (value.EndsWith(')') && value.Count(character => character == ')') > value.Count(character => character == '('))
        {
            value = value.Substring(0, value.Length - 1);
        }

        return value;
    }

}
