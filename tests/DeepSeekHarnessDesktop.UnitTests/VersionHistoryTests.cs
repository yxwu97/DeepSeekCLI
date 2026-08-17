using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Utilities;
using System.Reflection;

namespace DeepSeekHarnessDesktop.UnitTests;

public sealed class VersionHistoryTests
{
    [Fact]
    public void ParserReturnsEntriesAndChangesInSourceOrder()
    {
        const string markdown = """
            # 版本记录

            ## [1.2.0] - 2026-08-17

            ### 变更

            - 新增功能。
            - 修复问题。

            ### 其他

            - 不应显示。

            ## [1.1.0] - 2026-08-16

            ### 变更

            - 上一个版本。
            """;

        var entries = VersionHistoryParser.Parse(markdown);

        Assert.Collection(
            entries,
            current =>
            {
                Assert.Equal("1.2.0", current.Version);
                Assert.Equal("2026-08-17", current.Date);
                Assert.Equal(["新增功能。", "修复问题。"], current.Changes);
            },
            previous =>
            {
                Assert.Equal("1.1.0", previous.Version);
                Assert.Equal(["上一个版本。"], previous.Changes);
            });
    }

    [Fact]
    public void EmbeddedHistoryStartsWithCurrentApplicationVersion()
    {
        var entries = new VersionHistoryProvider().GetEntries();
        var informationalVersion = typeof(VersionHistoryProvider).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.NotEmpty(entries);
        Assert.Equal(informationalVersion, entries[0].Version);
        Assert.NotEmpty(entries[0].Changes);
    }
}
