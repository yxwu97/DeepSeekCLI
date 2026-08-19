using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Utilities;

public static class OutputLineProcessor
{
    public const int MaximumLineLength = 16 * 1024;
    public const string TruncationMarker = " [truncated]";

    public static string? Normalize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var cleaned = OscPattern.Replace(CsiPattern.Replace(line, string.Empty), string.Empty).Trim();
        if (cleaned.Length <= MaximumLineLength)
        {
            return cleaned;
        }

        return cleaned.Substring(0, MaximumLineLength - TruncationMarker.Length) + TruncationMarker;
    }

    private static readonly Regex CsiPattern = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex OscPattern = new(
        @"\x1B\](?:[^\x07\x1B]|\x1B(?!\\))*(?:\x07|\x1B\\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
