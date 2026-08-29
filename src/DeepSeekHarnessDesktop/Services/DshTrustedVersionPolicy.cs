using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using NuGet.Versioning;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshTrustedVersionPolicy : IDshTrustedVersionPolicy
{
    private const long MaximumStateBytes = 32 * 1024;
    private readonly Func<string> _stateRootProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DshRuntimeDescriptor _current;

    public DshTrustedVersionPolicy(Func<string>? stateRootProvider = null)
    {
        _stateRootProvider = stateRootProvider ?? DefaultStateRoot;
        Bootstrap = new DshRuntimeDescriptor(
            DshPackageMetadata.BootstrapVersion,
            DshPackageMetadata.RuntimeProtocol,
            DshPackageMetadata.MinimumDesktopVersion,
            DshPackageMetadata.SupportedNodeVersionRange,
            "bootstrap",
            DshPackageMetadata.BootstrapEvidenceSha256);
        _current = TryReadSelected() ?? Bootstrap;
    }

    public DshRuntimeDescriptor Bootstrap { get; }
    public DshRuntimeDescriptor Current => Volatile.Read(ref _current);

    public async Task SelectAsync(
        DshRuntimeDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ValidateDescriptor(descriptor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var root = Path.GetFullPath(_stateRootProvider());
            Directory.CreateDirectory(root);
            RejectReparsePoint(root);
            var statePath = Path.Combine(root, "selected-runtime.json");
            var backupPath = Path.Combine(root, "selected-runtime.backup.json");
            var temporaryPath = Path.Combine(root, $"selected-runtime.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, descriptor, cancellationToken: cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(statePath))
                {
                    File.Replace(temporaryPath, statePath, backupPath, true);
                }
                else
                {
                    File.Move(temporaryPath, statePath);
                }
                Volatile.Write(ref _current, descriptor);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private DshRuntimeDescriptor? TryReadSelected()
    {
        try
        {
            var root = Path.GetFullPath(_stateRootProvider());
            if (!Directory.Exists(root) || IsReparsePoint(root))
            {
                return null;
            }
            foreach (var path in new[]
            {
                Path.Combine(root, "selected-runtime.json"),
                Path.Combine(root, "selected-runtime.backup.json"),
            })
            {
                if (!File.Exists(path)
                    || IsReparsePoint(path)
                    || new FileInfo(path).Length > MaximumStateBytes)
                {
                    continue;
                }
                using var stream = File.OpenRead(path);
                var descriptor = JsonSerializer.Deserialize<DshRuntimeDescriptor>(stream);
                if (descriptor is not null)
                {
                    ValidateDescriptor(descriptor);
                    return descriptor;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
        }
        return null;
    }

    private static void ValidateDescriptor(DshRuntimeDescriptor descriptor)
    {
        if (!NuGetVersion.TryParse(descriptor.Version, out _)
            || descriptor.RuntimeProtocol != DshPackageMetadata.RuntimeProtocol
            || string.IsNullOrWhiteSpace(descriptor.MinimumDesktopVersion)
            || string.IsNullOrWhiteSpace(descriptor.NodeVersionRange)
            || string.IsNullOrWhiteSpace(descriptor.Source)
            || descriptor.EvidenceSha256.Length != 64
            || descriptor.EvidenceSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new HarnessException(DshUpdateErrorMapper.ProtocolIncompatible(
                "The selected DSH runtime descriptor is invalid or incompatible."));
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if (IsReparsePoint(path))
        {
            throw new IOException("The trusted DSH state root is a reparse point.");
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string DefaultStateRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepSeekHarnessDesktop",
        "dsh-runtime");
}
