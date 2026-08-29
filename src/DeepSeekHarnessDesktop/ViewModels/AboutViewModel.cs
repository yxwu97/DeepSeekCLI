using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using NuGet.Versioning;

namespace DeepSeekHarnessDesktop.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IDependencyDiagnosticsService _diagnosticsService;
    private readonly IDshReleaseService _releaseService;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly IDshTrustedVersionPolicy _trustedVersions;
    private readonly IHarnessLifecycleCoordinator? _coordinator;
    private readonly IUserConfirmationService? _confirmation;
    private readonly IDshUpdateCheckService? _updateCheckService;
    private readonly IDshRuntimeUpdateCoordinator? _runtimeUpdateCoordinator;

    [ObservableProperty]
    private DependencyDiagnosticsResult _diagnostics;

    [ObservableProperty]
    private DshUpdateCheckResult? _updateResult;

    [ObservableProperty]
    private DshCombinedUpdateCheckResult? _combinedUpdateResult;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _downloadStatus = "未执行 DSH 私有更新。";

    public AboutViewModel(
        IDependencyDiagnosticsService diagnosticsService,
        IDshReleaseService releaseService,
        IExternalLinkLauncher linkLauncher,
        IVersionHistoryProvider versionHistoryProvider,
        DependencyDiagnosticsResult diagnostics,
        IDshTrustedVersionPolicy? trustedVersions = null,
        IHarnessLifecycleCoordinator? coordinator = null,
        IUserConfirmationService? confirmation = null,
        IDshUpdateCheckService? updateCheckService = null,
        IDshRuntimeUpdateCoordinator? runtimeUpdateCoordinator = null)
    {
        _diagnosticsService = diagnosticsService;
        _releaseService = releaseService;
        _linkLauncher = linkLauncher;
        _trustedVersions = trustedVersions ?? new DshTrustedVersionPolicy();
        _coordinator = coordinator;
        _confirmation = confirmation;
        _updateCheckService = updateCheckService;
        _runtimeUpdateCoordinator = runtimeUpdateCoordinator;
        _diagnostics = diagnostics;
        VersionHistory = versionHistoryProvider.GetEntries();
        RefreshDiagnosticsCommand = new AsyncRelayCommand(RefreshDiagnosticsAsync, () => !IsBusy);
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, () => !IsBusy);
        DownloadAndUpdateCommand = new AsyncRelayCommand(
            DownloadAndUpdateAsync,
            CanDownloadAndUpdate);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        OpenDocumentationCommand = new RelayCommand(() => linkLauncher.Open(OfficialResource.DshDocumentation));
        OpenNpmPackageCommand = new RelayCommand(() => linkLauncher.Open(OfficialResource.NpmPackage));
        OpenDesktopGitHubCommand = new RelayCommand(() => linkLauncher.Open(OfficialResource.DesktopGitHub));
    }

    public IAsyncRelayCommand RefreshDiagnosticsCommand { get; }
    public IAsyncRelayCommand CheckUpdateCommand { get; }
    public IAsyncRelayCommand DownloadAndUpdateCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand OpenDocumentationCommand { get; }
    public IRelayCommand OpenNpmPackageCommand { get; }
    public IRelayCommand OpenDesktopGitHubCommand { get; }
    public IReadOnlyList<VersionHistoryEntry> VersionHistory { get; }

    public string DesktopVersion => Diagnostics.DesktopVersion;
    public string DotNetVersion => Diagnostics.DotNetVersion;
    public string? WebView2RuntimeVersion => Diagnostics.WebView2RuntimeVersion;
    public string? NodeVersion => Diagnostics.NodeVersion;
    public string? NpxPath => Diagnostics.NpxPath;
    public string? DshPath => Diagnostics.DshPath;
    public string DshVersion => Diagnostics.DshVersion ?? "未检测到";
    public string ValidatedDshVersion => _trustedVersions.Current.Version;
    public string LatestVersion => CombinedUpdateResult?.NpmLatestVersion
        ?? UpdateResult?.LatestVersion
        ?? "尚未检查";
    public string CatalogUpdateVersion => CombinedUpdateResult?.CatalogUpdate?.Entry.Version ?? "无";
    public string UpdateStatus => CombinedUpdateResult is { } combined
        ? FormatCombinedUpdateStatus(combined)
        : UpdateResult switch
        {
            null => "仅在点击“检查更新”时访问 npm 官方 registry。",
            { Succeeded: false } result => result.ErrorMessage ?? "检查更新失败。",
            { Succeeded: true } result => FormatUpdateStatus(result.LatestVersion!),
        };
    public string CheckedAt => CombinedUpdateResult?.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss")
        ?? (UpdateResult is null ? "-" : UpdateResult.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    public bool HasSelectedRuntime => string.Equals(
        Diagnostics.DshVersion,
        _trustedVersions.Current.Version,
        StringComparison.Ordinal);

    partial void OnDiagnosticsChanged(DependencyDiagnosticsResult value)
    {
        OnPropertyChanged(nameof(DesktopVersion));
        OnPropertyChanged(nameof(DotNetVersion));
        OnPropertyChanged(nameof(WebView2RuntimeVersion));
        OnPropertyChanged(nameof(NodeVersion));
        OnPropertyChanged(nameof(NpxPath));
        OnPropertyChanged(nameof(DshPath));
        OnPropertyChanged(nameof(DshVersion));
        OnPropertyChanged(nameof(HasSelectedRuntime));
        DownloadAndUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnUpdateResultChanged(DshUpdateCheckResult? value)
    {
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(CheckedAt));
    }

    partial void OnCombinedUpdateResultChanged(DshCombinedUpdateCheckResult? value)
    {
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(CatalogUpdateVersion));
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(CheckedAt));
        DownloadAndUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshDiagnosticsCommand.NotifyCanExecuteChanged();
        CheckUpdateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        DownloadAndUpdateCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshDiagnosticsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            Diagnostics = await _diagnosticsService.DiagnoseAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckUpdateAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            if (_updateCheckService is null)
            {
                UpdateResult = await _releaseService.CheckLatestAsync(cancellationToken);
            }
            else
            {
                CombinedUpdateResult = await _updateCheckService.CheckAsync(
                    Diagnostics.DesktopVersion,
                    Diagnostics.NodeVersion,
                    cancellationToken);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDownloadAndUpdate() => !IsBusy
        && _coordinator is not null
        && _confirmation is not null
        && Diagnostics.Node.Status == DependencyStatus.Available
        && Diagnostics.Npm.Status == DependencyStatus.Available
        && (CombinedUpdateResult?.CatalogUpdate is null || _runtimeUpdateCoordinator is not null)
        && (!HasSelectedRuntime || CombinedUpdateResult?.CatalogUpdate is not null);

    private async Task DownloadAndUpdateAsync(CancellationToken cancellationToken)
    {
        if (_coordinator is null || _confirmation is null || !_confirmation.ConfirmDshDownload())
        {
            DownloadStatus = "已取消 DSH 私有更新。";
            return;
        }
        if (_coordinator.Current.State == HarnessRuntimeState.RunningExternal)
        {
            DownloadStatus = "检测到外部 DSH 正在运行；不会停止外部进程，请退出后重试。";
            return;
        }

        IsBusy = true;
        var target = CombinedUpdateResult?.CatalogUpdate;
        DownloadStatus = $"正在下载并验证 DSH {target?.Entry.Version ?? _trustedVersions.Current.Version}...";
        try
        {
            if (target is not null)
            {
                if (_runtimeUpdateCoordinator is null)
                {
                    DownloadStatus = "签名目录安装服务尚未配置，未更改当前版本。";
                    return;
                }
                await _runtimeUpdateCoordinator.ApplyAsync(target, Diagnostics, cancellationToken);
                OnPropertyChanged(nameof(ValidatedDshVersion));
                CombinedUpdateResult = CombinedUpdateResult! with
                {
                    CurrentVersion = target.Entry.Version,
                    CatalogUpdate = null,
                };
            }
            else if (_coordinator.Current.State == HarnessRuntimeState.RunningOwned)
            {
                await _coordinator.RestartAsync(cancellationToken);
            }
            else
            {
                await _coordinator.StartAsync(cancellationToken);
            }
            Diagnostics = await _diagnosticsService.DiagnoseAsync(cancellationToken);
            DownloadStatus = HasSelectedRuntime
                ? $"DSH {_trustedVersions.Current.Version} 已验证并激活。"
                : _coordinator.Current.Error is { } error
                    ? $"{error.Code} · {error.UserMessage}"
                    : "DSH 更新未完成，请查看安装日志。";
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "DSH 更新已取消，原有版本保持不变。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Cancel()
    {
        RefreshDiagnosticsCommand.Cancel();
        CheckUpdateCommand.Cancel();
        DownloadAndUpdateCommand.Cancel();
    }

    private string FormatUpdateStatus(string latestText)
    {
        var validated = NuGetVersion.Parse(_trustedVersions.Current.Version);
        var latest = NuGetVersion.Parse(latestText);
        var relation = latest > validated
            ? "上游有尚未进入签名目录的版本，当前继续使用已选择版本。"
            : latest == validated
                ? "npm 当前版本与当前选择版本一致。"
                : "当前选择版本高于 npm latest，自动启动仍使用选择版本。";
        return $"npm 当前发布版本为 {latestText}；{relation}";
    }

    private static string FormatCombinedUpdateStatus(DshCombinedUpdateCheckResult result)
    {
        if (result.CatalogUpdate is { } update)
        {
            var suffix = result.WaitingForValidation
                ? "；npm 还有更新版本等待签名验证"
                : string.Empty;
            return $"签名目录可更新到 {update.Entry.Version}{suffix}。";
        }
        if (result.CatalogError is { } catalogError)
        {
            var npm = result.NpmLatestVersion is null
                ? result.NpmError ?? "npm latest 不可用"
                : $"npm latest 为 {result.NpmLatestVersion}";
            return $"{npm}；{catalogError.Code} · {catalogError.UserMessage}";
        }
        if (result.WaitingForValidation && result.NpmLatestVersion is { } latest)
        {
            return $"npm latest 为 {latest}，尚未进入受信签名目录，不能安装。";
        }
        return result.NpmLatestVersion is { } current
            ? $"npm latest 为 {current}；签名目录没有更高的兼容版本。"
            : result.NpmError ?? "更新检查未返回可用结果。";
    }
}
