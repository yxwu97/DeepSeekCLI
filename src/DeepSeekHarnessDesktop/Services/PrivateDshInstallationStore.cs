using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeepSeekHarnessDesktop.Services;

public sealed record PrivateDshInstallTransaction(
    string StagingPath,
    string InstallId,
    string LockSha256);

public sealed class PrivateDshInstallationStore : IPrivateDshInstallationStore
{
    private const long MaximumStateBytes = 32 * 1024;
    private const long MaximumPackageBytes = 64 * 1024;
    private const long MaximumLockBytes = 4 * 1024 * 1024;
    private readonly Func<string> _rootProvider;
    private readonly Func<string> _resourceRootProvider;

    public PrivateDshInstallationStore(
        Func<string>? rootProvider = null,
        Func<string>? resourceRootProvider = null)
    {
        _rootProvider = rootProvider ?? DefaultRoot;
        _resourceRootProvider = resourceRootProvider
            ?? (() => Path.Combine(AppContext.BaseDirectory, "dsh-runtime"));
    }

    public async Task<DshInstallationCandidate?> FindActiveAsync(
        string? nodePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nodePath) || !IsRegularFile(nodePath!))
        {
            return null;
        }

        var root = GetRoot();
        if (!Directory.Exists(root) || IsReparsePoint(root))
        {
            return null;
        }

        foreach (var statePath in new[] { ActivePath(root), ActiveBackupPath(root) })
        {
            var state = await TryReadStateAsync<InstallState>(statePath, cancellationToken);
            if (state is null)
            {
                continue;
            }

            var candidate = await ValidateVersionAsync(root, state, nodePath!, cancellationToken);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<PrivateDshInstallTransaction> CreateTransactionAsync(
        CancellationToken cancellationToken)
    {
        var root = GetRoot();
        EnsureControlledDirectory(root);
        var stagingRoot = Path.Combine(root, "staging");
        var versionsRoot = Path.Combine(root, "versions");
        EnsureControlledDirectory(stagingRoot);
        EnsureControlledDirectory(versionsRoot);

        var resources = await ValidateResourcesAsync(cancellationToken);
        var installId = BuildInstallId(resources.LockSha256);
        var stagingPath = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        EnsureDirectChild(stagingRoot, stagingPath);
        Directory.CreateDirectory(stagingPath);
        try
        {
            await CopyFileAsync(resources.PackagePath, Path.Combine(stagingPath, "package.json"), cancellationToken);
            await CopyFileAsync(resources.LockPath, Path.Combine(stagingPath, "package-lock.json"), cancellationToken);
            return new PrivateDshInstallTransaction(stagingPath, installId, resources.LockSha256);
        }
        catch
        {
            DeleteTreeSafely(stagingPath);
            throw;
        }
    }

    public async Task<DshInstallationCandidate> CommitVersionAsync(
        PrivateDshInstallTransaction transaction,
        string nodePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetRoot();
        ValidateTransaction(root, transaction);
        await ValidateInstalledGraphAsync(transaction, cancellationToken);
        var state = new InstallState(
            transaction.InstallId,
            DshPackageMetadata.ValidatedVersion,
            transaction.LockSha256);
        await WriteJsonAsync(
            Path.Combine(transaction.StagingPath, "install.json"),
            state,
            cancellationToken);

        var versionsRoot = Path.Combine(root, "versions");
        var versionPath = Path.Combine(versionsRoot, transaction.InstallId);
        EnsureDirectChild(versionsRoot, versionPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(versionPath))
        {
            try
            {
                Directory.Move(transaction.StagingPath, versionPath);
            }
            catch (IOException) when (Directory.Exists(versionPath))
            {
                return await ReuseConcurrentCommitAsync(
                    root,
                    versionPath,
                    transaction,
                    state,
                    nodePath,
                    cancellationToken);
            }
        }
        else
        {
            var existing = await ValidateVersionAsync(root, state, nodePath, cancellationToken);
            if (existing is null)
            {
                throw StoreError("现有 DSH 私有安装已损坏", "Existing private DSH version failed validation.");
            }
            DeleteTreeSafely(transaction.StagingPath);
            return existing;
        }

        return await ValidateVersionAsync(root, state, nodePath, cancellationToken)
            ?? throw StoreError("DSH 私有安装校验失败", "Committed private DSH version failed validation.");
    }

    private async Task<DshInstallationCandidate> ReuseConcurrentCommitAsync(
        string root,
        string versionPath,
        PrivateDshInstallTransaction transaction,
        InstallState state,
        string nodePath,
        CancellationToken cancellationToken)
    {
        var existing = await ValidateVersionAsync(root, state, nodePath, cancellationToken);
        if (existing is null)
        {
            throw StoreError(
                "现有 DSH 私有安装已损坏",
                $"Concurrent private DSH commit is invalid: {versionPath}");
        }
        DeleteTreeSafely(transaction.StagingPath);
        return existing;
    }

    public Task ActivateAsync(
        DshInstallationCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.Source != DshInstallationSource.Private
            || string.IsNullOrWhiteSpace(candidate.InstallId))
        {
            throw new ArgumentException("Only a validated private DSH candidate can be activated.");
        }

        var root = GetRoot();
        var lockPath = Path.Combine(
            root,
            "versions",
            candidate.InstallId!,
            "package-lock.json");
        return ActivateCoreAsync(candidate, lockPath, cancellationToken);
    }

    public Task CleanupAsync(PrivateDshInstallTransaction transaction)
    {
        var root = GetRoot();
        EnsureDirectChild(Path.Combine(root, "staging"), transaction.StagingPath);
        if (!Directory.Exists(transaction.StagingPath))
        {
            return Task.CompletedTask;
        }
        if (IsReparsePoint(transaction.StagingPath))
        {
            throw StoreError("DSH 安装暂存目录无效", "The staging transaction is a reparse point.");
        }
        DeleteTreeSafely(transaction.StagingPath);
        return Task.CompletedTask;
    }

    private async Task ActivateCoreAsync(
        DshInstallationCandidate candidate,
        string lockPath,
        CancellationToken cancellationToken)
    {
        var digest = await ComputeSha256Async(lockPath, cancellationToken);
        var state = new InstallState(candidate.InstallId!, candidate.Version, digest);
        var root = GetRoot();
        EnsureControlledDirectory(root);
        var activePath = ActivePath(root);
        if (File.Exists(activePath) && IsReparsePoint(activePath))
        {
            throw StoreError("DSH 私有安装状态无效", "active.json is a reparse point.");
        }

        var temporaryPath = Path.Combine(root, $"active.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteJsonAsync(temporaryPath, state, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(activePath))
            {
                File.Replace(temporaryPath, activePath, ActiveBackupPath(root), true);
            }
            else
            {
                File.Move(temporaryPath, activePath);
            }
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            throw;
        }
    }

    private static async Task<DshInstallationCandidate?> ValidateVersionAsync(
        string root,
        InstallState state,
        string nodePath,
        CancellationToken cancellationToken)
    {
        if (!IsValidState(state) || state.InstallId != BuildInstallId(state.LockSha256))
        {
            return null;
        }

        var versionsRoot = Path.Combine(root, "versions");
        var versionRoot = Path.Combine(versionsRoot, state.InstallId);
        try
        {
            EnsureDirectChild(versionsRoot, versionRoot);
            var packageRoot = Path.Combine(versionRoot, "node_modules", "@deepseek-ai", "dsh");
            var manifestPath = Path.Combine(packageRoot, "package.json");
            var entryPoint = Path.Combine(packageRoot, "lib", "bin.js");
            var markerPath = Path.Combine(versionRoot, "install.json");
            var lockPath = Path.Combine(versionRoot, "package-lock.json");
            if (!AllRegularAndUnlinked(
                    root, versionsRoot, versionRoot,
                    Path.Combine(versionRoot, "node_modules"),
                    Path.Combine(versionRoot, "node_modules", "@deepseek-ai"),
                    packageRoot, Path.Combine(packageRoot, "lib"),
                    manifestPath, entryPoint, markerPath, lockPath))
            {
                return null;
            }

            var marker = await TryReadStateAsync<InstallState>(markerPath, cancellationToken);
            if (marker != state || !await IsExpectedManifestAsync(manifestPath, cancellationToken))
            {
                return null;
            }
            if (!string.Equals(
                    await ComputeSha256Async(lockPath, cancellationToken),
                    state.LockSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new DshInstallationCandidate(
                DshInstallationSource.Private,
                nodePath,
                entryPoint,
                state.Version,
                state.InstallId);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            return null;
        }
    }

    private async Task ValidateInstalledGraphAsync(
        PrivateDshInstallTransaction transaction,
        CancellationToken cancellationToken)
    {
        var lockPath = Path.Combine(transaction.StagingPath, "package-lock.json");
        if (!string.Equals(
                await ComputeSha256Async(lockPath, cancellationToken),
                transaction.LockSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw StoreError("DSH 锁文件校验失败", "package-lock.json changed during npm ci.");
        }

        var manifest = Path.Combine(
            transaction.StagingPath,
            "node_modules",
            "@deepseek-ai",
            "dsh",
            "package.json");
        var entryPoint = Path.Combine(
            transaction.StagingPath,
            "node_modules",
            "@deepseek-ai",
            "dsh",
            "lib",
            "bin.js");
        if (!IsRegularFile(entryPoint) || !await IsExpectedManifestAsync(manifest, cancellationToken))
        {
            throw StoreError("DSH 安装内容校验失败", "The installed DSH entry point or manifest is invalid.");
        }
    }

    private async Task<ResourceState> ValidateResourcesAsync(CancellationToken cancellationToken)
    {
        var resourceRoot = Path.GetFullPath(_resourceRootProvider());
        var packagePath = Path.Combine(resourceRoot, "package.json");
        var lockPath = Path.Combine(resourceRoot, "package-lock.json");
        if (!IsRegularFile(packagePath) || !IsRegularFile(lockPath)
            || new FileInfo(packagePath).Length > MaximumPackageBytes
            || new FileInfo(lockPath).Length > MaximumLockBytes
            || IsReparsePoint(resourceRoot))
        {
            throw StoreError("DSH 安装资源缺失或无效", "Trusted package.json/package-lock.json resources are unavailable.");
        }

        using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
        using (var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken))
        {
            if (!document.RootElement.TryGetProperty("dependencies", out var dependencies)
                || !dependencies.TryGetProperty(DshPackageMetadata.PackageName, out var version)
                || !string.Equals(version.GetString(), DshPackageMetadata.ValidatedVersion, StringComparison.Ordinal))
            {
                throw StoreError("DSH 安装资源版本无效", "Trusted package.json does not pin the validated DSH version.");
            }
        }
        await ValidateLockRootAsync(lockPath, cancellationToken);

        return new ResourceState(
            packagePath,
            lockPath,
            await ComputeSha256Async(lockPath, cancellationToken));
    }

    private static async Task ValidateLockRootAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("lockfileVersion", out var lockVersion)
            || lockVersion.GetInt32() != 3
            || !document.RootElement.TryGetProperty("packages", out var packages)
            || !packages.TryGetProperty(string.Empty, out var rootPackage)
            || !rootPackage.TryGetProperty("dependencies", out var dependencies)
            || !dependencies.TryGetProperty(DshPackageMetadata.PackageName, out var version)
            || !string.Equals(version.GetString(), DshPackageMetadata.ValidatedVersion, StringComparison.Ordinal))
        {
            throw StoreError(
                "DSH 锁文件根依赖无效",
                "Trusted package-lock.json does not pin the validated root DSH version.");
        }
    }

    private static async Task<bool> IsExpectedManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!IsRegularFile(manifestPath) || new FileInfo(manifestPath).Length > MaximumPackageBytes)
        {
            return false;
        }
        using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return root.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), DshPackageMetadata.PackageName, StringComparison.Ordinal)
            && root.TryGetProperty("version", out var version)
            && string.Equals(version.GetString(), DshPackageMetadata.ValidatedVersion, StringComparison.Ordinal)
            && root.TryGetProperty("bin", out var bin)
            && bin.TryGetProperty("dsh", out var entry)
            && string.Equals(entry.GetString(), "lib/bin.js", StringComparison.Ordinal);
    }

    private static async Task<T?> TryReadStateAsync<T>(
        string path,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            if (!IsRegularFile(path) || new FileInfo(path).Length > MaximumStateBytes)
            {
                return null;
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, 81920, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
        {
            sha.TransformBlock(buffer, 0, count, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(sha.Hash!).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void ValidateTransaction(string root, PrivateDshInstallTransaction transaction)
    {
        var stagingRoot = Path.Combine(root, "staging");
        EnsureDirectChild(stagingRoot, transaction.StagingPath);
        if (IsReparsePoint(transaction.StagingPath))
        {
            throw StoreError("DSH 安装暂存目录无效", "The staging transaction is a reparse point.");
        }
    }

    private static void EnsureControlledDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (IsReparsePoint(path))
        {
            throw StoreError("DSH 私有安装目录无效", $"Controlled directory is a reparse point: {path}");
        }
    }

    private static void EnsureDirectChild(string parent, string child)
    {
        var expectedParent = PathCompatibility.TrimEndingDirectorySeparator(parent);
        var actualParent = PathCompatibility.TrimEndingDirectorySeparator(
            Path.GetDirectoryName(Path.GetFullPath(child))
                ?? throw new ArgumentException("Path has no parent."));
        if (!string.Equals(expectedParent, actualParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Path is outside the controlled parent.");
        }
    }

    private static bool AllRegularAndUnlinked(params string[] paths) =>
        paths.All(path => (Directory.Exists(path) || File.Exists(path)) && !IsReparsePoint(path));

    private static bool IsRegularFile(string path)
    {
        try
        {
            return File.Exists(path)
                && (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    private static void DeleteTreeSafely(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        if (IsReparsePoint(path))
        {
            Directory.Delete(path, false);
            return;
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteTreeSafely(entry);
            }
            else
            {
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                File.Delete(entry);
            }
        }
        Directory.Delete(path, false);
    }

    private string GetRoot() => Path.GetFullPath(_rootProvider());
    private static string ActivePath(string root) => Path.Combine(root, "active.json");
    private static string ActiveBackupPath(string root) => Path.Combine(root, "active.json.bak");
    private static string BuildInstallId(string digest) =>
        $"{DshPackageMetadata.ValidatedVersion}-{digest.Substring(0, 16).ToLowerInvariant()}";
    private static bool IsValidState(InstallState state) =>
        string.Equals(state.Version, DshPackageMetadata.ValidatedVersion, StringComparison.Ordinal)
        && state.LockSha256.Length == 64
        && state.LockSha256.All(character => Uri.IsHexDigit(character))
        && state.InstallId.Length <= 96
        && state.InstallId.All(character => char.IsLetterOrDigit(character) || character is '.' or '-');
    private static string DefaultRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepSeekHarnessDesktop",
        "dsh");
    private static HarnessException StoreError(string userMessage, string technicalMessage) =>
        new(new HarnessError("DSH-E214", userMessage, technicalMessage, true));

    private sealed record InstallState(string InstallId, string Version, string LockSha256);
    private sealed record ResourceState(string PackagePath, string LockPath, string LockSha256);
}
