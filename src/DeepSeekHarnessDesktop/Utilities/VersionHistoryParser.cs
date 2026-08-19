using DeepSeekHarnessDesktop.Models;
using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Utilities;

public static class VersionHistoryParser
{
    private static readonly Regex VersionHeadingPattern = new(
        @"^## \[(?<version>\d+\.\d+\.\d+)\] - (?<date>\d{4}-\d{2}-\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<VersionHistoryEntry> Parse(string markdown)
    {
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));
        var entries = new List<VersionHistoryEntry>();
        var changes = new List<string>();
        string? version = null;
        string? date = null;
        var readingChanges = false;

        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            var heading = VersionHeadingPattern.Match(line);
            if (heading.Success)
            {
                AddEntry(entries, version, date, changes);
                version = heading.Groups["version"].Value;
                date = heading.Groups["date"].Value;
                changes = [];
                readingChanges = false;
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                readingChanges = line.Equals("### 变更", StringComparison.Ordinal);
                continue;
            }

            if (readingChanges && line.StartsWith("- ", StringComparison.Ordinal))
            {
                changes.Add(line.Substring(2).Trim());
            }
        }

        AddEntry(entries, version, date, changes);
        return entries;
    }

    private static void AddEntry(
        ICollection<VersionHistoryEntry> entries,
        string? version,
        string? date,
        IReadOnlyList<string> changes)
    {
        if (version is not null && date is not null)
        {
            entries.Add(new VersionHistoryEntry(version, date, changes.ToArray()));
        }
    }

}
