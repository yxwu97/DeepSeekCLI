using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Reflection;
using System.Text;

namespace DeepSeekHarnessDesktop.Services;

public sealed class VersionHistoryProvider : IVersionHistoryProvider
{
    private const string ResourceName = "DeepSeekHarnessDesktop.VERSION_HISTORY.md";

    public IReadOnlyList<VersionHistoryEntry> GetEntries()
    {
        var assembly = typeof(VersionHistoryProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return VersionHistoryParser.Parse(reader.ReadToEnd());
    }
}
