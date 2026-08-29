using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.Services;

public sealed class DshRuntimeUpdateCoordinator : IDshRuntimeUpdateCoordinator, IDisposable
{
    private readonly IHarnessLifecycleCoordinator _lifecycle;
    private readonly IDshCatalogUpdateInstaller _installer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DshRuntimeUpdateCoordinator(
        IHarnessLifecycleCoordinator lifecycle,
        IDshCatalogUpdateInstaller installer)
    {
        _lifecycle = lifecycle;
        _installer = installer;
    }

    public async Task<DshInstallationCandidate> ApplyAsync(
        DshCatalogUpdateCandidate target,
        DependencyDiagnosticsResult diagnostics,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        var restartOldRuntimeOnFailure = false;
        try
        {
            if (_lifecycle.Current.State == HarnessRuntimeState.RunningExternal)
            {
                throw new HarnessException(DshUpdateErrorMapper.ActivationValidationFailed(
                    "A signed DSH update cannot replace or stop an externally owned runtime."));
            }
            if (diagnostics.Node is not { Status: DependencyStatus.Available, Path: { } nodePath, Version: { } nodeVersion }
                || diagnostics.Npm is not { Status: DependencyStatus.Available, Path: { } npmPath })
            {
                throw new HarnessException(DshUpdateErrorMapper.ProtocolIncompatible(
                    "A signed DSH update requires validated node.exe and npm.cmd paths."));
            }

            if (_lifecycle.Current.State == HarnessRuntimeState.RunningOwned)
            {
                await _lifecycle.StopAsync(cancellationToken);
                restartOldRuntimeOnFailure = true;
            }
            var candidate = await _installer.InstallAsync(
                target,
                diagnostics.DesktopVersion,
                nodeVersion,
                nodePath,
                npmPath,
                cancellationToken);
            restartOldRuntimeOnFailure = false;
            await _lifecycle.StartAsync(cancellationToken);
            return candidate;
        }
        catch
        {
            if (restartOldRuntimeOnFailure)
            {
                try { await _lifecycle.StartAsync(CancellationToken.None); }
                catch (Exception exception) when (exception is not StackOverflowException)
                {
                }
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
