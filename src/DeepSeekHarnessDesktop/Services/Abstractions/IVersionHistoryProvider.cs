using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IVersionHistoryProvider
{
    IReadOnlyList<VersionHistoryEntry> GetEntries();
}
