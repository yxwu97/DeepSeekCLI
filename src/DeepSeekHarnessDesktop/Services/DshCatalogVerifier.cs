using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Utilities;
using NuGet.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshCatalogVerifier
{
    public const int MaximumCatalogBytes = 256 * 1024;
    public const int MaximumEntries = 64;
    private const int MaximumDepth = 8;
    private const int MaximumTextLength = 2048;
    private static readonly HashSet<string> RootFields =
    ["schemaVersion", "catalogSequence", "generatedAt", "signingKeyId", "entries"];
    private static readonly HashSet<string> EntryFields =
    [
        "version", "publishedAt", "runtimeProtocol", "minimumDesktopVersion",
        "nodeVersionRange", "revoked", "package", "lock",
    ];
    private static readonly HashSet<string> AssetFields = ["url", "bytes", "sha256"];
    private readonly IReadOnlyDictionary<string, RSAParameters> _keys;
    private readonly string _assetHost;

    public DshCatalogVerifier(
        IReadOnlyDictionary<string, RSAParameters> keys,
        string assetHost)
    {
        _keys = keys;
        _assetHost = assetHost;
    }

    public DshCatalogSnapshot VerifyAndParse(byte[] catalog, byte[] signature)
    {
        try
        {
            ValidateEnvelope(catalog, signature);
            var verifiedKeyId = VerifySignature(catalog, signature);
            ValidateStructure(catalog);
            using var document = JsonDocument.Parse(catalog, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth,
            });
            var root = document.RootElement;
            RequireFields(root, RootFields);
            var keyId = ReadText(root, "signingKeyId", 64);
            if (!string.Equals(keyId, verifiedKeyId, StringComparison.Ordinal))
            {
                throw Rejected("The catalog signingKeyId does not match its detached signature.");
            }

            var schema = ReadInt32(root, "schemaVersion", 1, 1);
            var sequence = ReadInt64(root, "catalogSequence", 1, long.MaxValue);
            var generatedAt = ReadDate(root, "generatedAt");
            var entriesElement = root.GetProperty("entries");
            if (entriesElement.ValueKind != JsonValueKind.Array
                || entriesElement.GetArrayLength() > MaximumEntries)
            {
                throw Rejected("The catalog entry array is invalid or too large.");
            }
            var entries = new List<DshCatalogEntry>();
            var versions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in entriesElement.EnumerateArray())
            {
                var entry = ParseEntry(element);
                if (!versions.Add(entry.Version))
                {
                    throw Rejected($"Duplicate catalog version: {entry.Version}.");
                }
                entries.Add(entry);
            }
            return new DshCatalogSnapshot(schema, sequence, generatedAt, keyId, entries);
        }
        catch (HarnessException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or CryptographicException or FormatException
                or InvalidOperationException or UriFormatException or OverflowException)
        {
            throw Rejected("The signed DSH catalog is malformed.", exception);
        }
    }

    private string VerifySignature(byte[] catalog, byte[] signature)
    {
        foreach (var pair in _keys)
        {
            var key = pair.Value;
            if (key.Modulus is null || key.Modulus.Length < 384)
            {
                continue;
            }
            using var rsa = RSA.Create();
            rsa.ImportParameters(key);
            if (rsa.VerifyData(
                catalog,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1))
            {
                return pair.Key;
            }
        }
        throw Rejected("The catalog detached signature is invalid or uses an unknown key.");
    }

    private DshCatalogEntry ParseEntry(JsonElement element)
    {
        RequireFields(element, EntryFields);
        var versionText = ReadText(element, "version", 64);
        if (!NuGetVersion.TryParse(versionText, out var version))
        {
            throw Rejected($"Invalid DSH catalog version: {versionText}.");
        }
        var revoked = element.GetProperty("revoked");
        if (revoked.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Rejected("The catalog revoked field must be boolean.");
        }
        var minimumDesktopVersion = ReadText(element, "minimumDesktopVersion", 64);
        var nodeVersionRange = ReadText(element, "nodeVersionRange", 128);
        if (!NuGetVersion.TryParse(minimumDesktopVersion, out _)
            || !DshCatalogCompatibility.IsSupportedNodeRange(nodeVersionRange))
        {
            throw Rejected("The catalog compatibility fields are invalid or unsupported.");
        }
        return new DshCatalogEntry(
            version.ToNormalizedString(),
            ReadDate(element, "publishedAt"),
            ReadInt32(element, "runtimeProtocol", 1, int.MaxValue),
            minimumDesktopVersion,
            nodeVersionRange,
            revoked.GetBoolean(),
            ParseAsset(element.GetProperty("package")),
            ParseAsset(element.GetProperty("lock")));
    }

    private DshCatalogAsset ParseAsset(JsonElement element)
    {
        RequireFields(element, AssetFields);
        var uri = new Uri(ReadText(element, "url", MaximumTextLength), UriKind.Absolute);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, _assetHost, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Rejected($"Catalog asset URL is outside the fixed HTTPS host: {uri.Host}.");
        }
        var bytes = ReadInt64(element, "bytes", 1, 16 * 1024 * 1024);
        var sha256 = ReadText(element, "sha256", 64);
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw Rejected("Catalog asset SHA-256 is invalid.");
        }
        return new DshCatalogAsset(uri, bytes, sha256.ToLowerInvariant());
    }

    private static void ValidateEnvelope(byte[] catalog, byte[] signature)
    {
        if (catalog.Length == 0 || catalog.Length > MaximumCatalogBytes
            || signature.Length == 0 || signature.Length > 1024
            || (catalog.Length >= 3 && catalog[0] == 0xef && catalog[1] == 0xbb && catalog[2] == 0xbf))
        {
            throw Rejected("The catalog envelope is empty, oversized, or contains a UTF-8 BOM.");
        }
    }

    private static void ValidateStructure(byte[] catalog)
    {
        var reader = new Utf8JsonReader(catalog, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumDepth,
        });
        var properties = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                properties.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                properties.Pop();
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString() ?? string.Empty;
                if (properties.Count == 0 || !properties.Peek().Add(name))
                {
                    throw Rejected($"Duplicate catalog property: {name}.");
                }
            }
        }
    }

    private static void RequireFields(JsonElement element, HashSet<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Rejected("A catalog object was expected.");
        }
        var actual = element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
        {
            throw Rejected("The catalog contains missing or unknown fields.");
        }
    }

    private static string ReadText(JsonElement element, string name, int maximumLength)
    {
        var property = element.GetProperty(name);
        var value = property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        if (string.IsNullOrWhiteSpace(value) || value!.Length > maximumLength)
        {
            throw Rejected($"Catalog field {name} is invalid.");
        }
        return value;
    }

    private static DateTimeOffset ReadDate(JsonElement element, string name) =>
        DateTimeOffset.Parse(
            ReadText(element, name, 64),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal);

    private static int ReadInt32(JsonElement element, string name, int minimum, int maximum)
    {
        var value = ReadInt64(element, name, minimum, maximum);
        return checked((int)value);
    }

    private static long ReadInt64(JsonElement element, string name, long minimum, long maximum)
    {
        var property = element.GetProperty(name);
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out var value)
            || value < minimum
            || value > maximum)
        {
            throw Rejected($"Catalog integer field {name} is invalid.");
        }
        return value;
    }

    private static HarnessException Rejected(string technical, Exception? exception = null) =>
        new(DshUpdateErrorMapper.CatalogRejected(technical, exception));
}
