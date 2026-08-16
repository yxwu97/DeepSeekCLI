using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Utilities;

public static partial class OutputLineProcessor
{
    public const int MaximumLineLength = 16 * 1024;
    public const string TruncationMarker = " [truncated]";

    public static string? Normalize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var cleaned = OscRegex().Replace(CsiRegex().Replace(line, string.Empty), string.Empty).Trim();
        if (cleaned.Length <= MaximumLineLength)
        {
            return cleaned;
        }

        return string.Concat(cleaned.AsSpan(0, MaximumLineLength - TruncationMarker.Length), TruncationMarker);
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant)]
    private static partial Regex CsiRegex();

    [GeneratedRegex(@"\x1B\](?:[^\x07\x1B]|\x1B(?!\\))*(?:\x07|\x1B\\)", RegexOptions.CultureInvariant)]
    private static partial Regex OscRegex();
}
