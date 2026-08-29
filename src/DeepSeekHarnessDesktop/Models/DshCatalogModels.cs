namespace DeepSeekHarnessDesktop.Models;

public sealed record DshCatalogAsset(
    Uri Url,
    long Bytes,
    string Sha256);

public sealed record DshCatalogEntry(
    string Version,
    DateTimeOffset PublishedAt,
    int RuntimeProtocol,
    string MinimumDesktopVersion,
    string NodeVersionRange,
    bool Revoked,
    DshCatalogAsset Package,
    DshCatalogAsset Lock);

public sealed record DshCatalogSnapshot(
    int SchemaVersion,
    long CatalogSequence,
    DateTimeOffset GeneratedAt,
    string SigningKeyId,
    IReadOnlyList<DshCatalogEntry> Entries);

public sealed record DshCatalogLoadResult(
    DshCatalogSnapshot? Snapshot,
    bool FromCache,
    DateTimeOffset CheckedAt,
    HarnessError? Error = null)
{
    public bool Succeeded => Snapshot is not null && Error is null;
}

public sealed record DshCatalogUpdateCandidate(
    DshCatalogEntry Entry,
    DshRuntimeDescriptor Descriptor,
    long CatalogSequence);

public sealed record DshCombinedUpdateCheckResult(
    string CurrentVersion,
    string? NpmLatestVersion,
    DshCatalogUpdateCandidate? CatalogUpdate,
    bool WaitingForValidation,
    DateTimeOffset CheckedAt,
    string? NpmError = null,
    HarnessError? CatalogError = null);

public sealed record DshDownloadedAssets(
    string RootPath,
    string PackagePath,
    string LockPath);
