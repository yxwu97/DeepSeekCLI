using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Utilities;
using NuGet.Versioning;
using System.Text.RegularExpressions;

namespace DeepSeekHarnessDesktop.Services;

public static class DshCatalogCompatibility
{
    private static readonly Regex NodeRangePattern = new(
        @"^>=(?<minimum>[0-9]+(?:\.[0-9]+(?:\.[0-9]+)?)?)\s+<(?<maximum>[0-9]+(?:\.[0-9]+(?:\.[0-9]+)?)?)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    public static DshCatalogUpdateCandidate? SelectUpdate(
        DshCatalogSnapshot snapshot,
        DshRuntimeDescriptor current,
        string desktopVersion,
        string? nodeVersion,
        out HarnessError? compatibilityError)
    {
        compatibilityError = null;
        if (!NuGetVersion.TryParse(current.Version, out var currentVersion)
            || !NuGetVersion.TryParse(desktopVersion, out var desktop)
            || !TryParseNodeVersion(nodeVersion, out var node))
        {
            compatibilityError = DshUpdateErrorMapper.ProtocolIncompatible(
                "Desktop or Node.js version information is unavailable for signed catalog filtering.");
            return null;
        }

        var upgrades = snapshot.Entries
            .Select(entry => new { Entry = entry, Version = NuGetVersion.Parse(entry.Version) })
            .Where(item => item.Version > currentVersion && !item.Entry.Revoked)
            .OrderByDescending(item => item.Version)
            .ToList();
        foreach (var item in upgrades)
        {
            if (item.Entry.RuntimeProtocol != DshPackageMetadata.RuntimeProtocol
                || !NuGetVersion.TryParse(item.Entry.MinimumDesktopVersion, out var minimumDesktop)
                || desktop < minimumDesktop
                || !IsNodeVersionInRange(node!, item.Entry.NodeVersionRange))
            {
                continue;
            }

            var descriptor = new DshRuntimeDescriptor(
                item.Entry.Version,
                item.Entry.RuntimeProtocol,
                item.Entry.MinimumDesktopVersion,
                item.Entry.NodeVersionRange,
                $"catalog:{snapshot.CatalogSequence}",
                item.Entry.Lock.Sha256);
            return new DshCatalogUpdateCandidate(
                item.Entry,
                descriptor,
                snapshot.CatalogSequence);
        }

        if (upgrades.Count > 0)
        {
            compatibilityError = DshUpdateErrorMapper.ProtocolIncompatible(
                "The signed catalog contains a newer DSH runtime, but none is compatible with this Desktop and Node.js runtime.");
        }
        return null;
    }

    public static bool IsSupportedNodeRange(string value) => TryParseNodeRange(value, out _, out _);

    private static bool IsNodeVersionInRange(NuGetVersion node, string range) =>
        TryParseNodeRange(range, out var minimum, out var maximum)
        && node >= minimum
        && node < maximum;

    private static bool TryParseNodeVersion(string? value, out NuGetVersion? version)
    {
        var normalized = value?.Trim();
        if (normalized?.StartsWith("v", StringComparison.OrdinalIgnoreCase) == true)
        {
            normalized = normalized.Substring(1);
        }
        return NuGetVersion.TryParse(normalized, out version);
    }

    private static bool TryParseNodeRange(
        string value,
        out NuGetVersion minimum,
        out NuGetVersion maximum)
    {
        var match = NodeRangePattern.Match(value);
        if (match.Success
            && NuGetVersion.TryParse(match.Groups["minimum"].Value, out var parsedMinimum)
            && NuGetVersion.TryParse(match.Groups["maximum"].Value, out var parsedMaximum)
            && parsedMinimum < parsedMaximum)
        {
            minimum = parsedMinimum;
            maximum = parsedMaximum;
            return true;
        }
        minimum = new NuGetVersion(0, 0, 0);
        maximum = minimum;
        return false;
    }
}
