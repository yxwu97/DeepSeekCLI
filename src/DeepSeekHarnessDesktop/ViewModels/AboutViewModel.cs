using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IDependencyDiagnosticsService _diagnosticsService;
    private readonly IDshReleaseService _releaseService;
    private readonly IExternalLinkLauncher _linkLauncher;

    [ObservableProperty]
    private DependencyDiagnosticsResult _diagnostics;

    [ObservableProperty]
    private DshUpdateCheckResult? _updateResult;

    [ObservableProperty]
    private bool _isBusy;

    public AboutViewModel(
        IDependencyDiagnosticsService diagnosticsService,
        IDshReleaseService releaseService,
        IExternalLinkLauncher linkLauncher,
        IVersionHistoryProvider versionHistoryProvider,
        DependencyDiagnosticsResult diagnostics)
    {
        _diagnosticsService = diagnosticsService;
        _releaseService = releaseService;
        _linkLauncher = linkLauncher;
        _diagnostics = diagnostics;
        VersionHistory = versionHistoryProvider.GetEntries();
        RefreshDiagnosticsCommand = new AsyncRelayCommand(RefreshDiagnosticsAsync, () => !IsBusy);
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        OpenDocumentationCommand = new RelayCommand(() => linkLauncher.Open(OfficialResource.DshDocumentation));
        OpenNpmPackageCommand = new RelayCommand(() => linkLauncher.Open(OfficialResource.NpmPackage));
        OpenDesktopGitHubCommand = new RelayCommand(() => linkLauncher.Open(OfficialResource.DesktopGitHub));
    }

    public IAsyncRelayCommand RefreshDiagnosticsCommand { get; }
    public IAsyncRelayCommand CheckUpdateCommand { get; }
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
    public string LatestVersion => UpdateResult?.LatestVersion ?? "尚未检查";
    public string UpdateStatus => UpdateResult switch
    {
        null => "仅在点击“检查更新”时访问 npm 官方 registry。",
        { Succeeded: false } result => result.ErrorMessage ?? "检查更新失败。",
        { Succeeded: true } result => $"npm 当前发布版本为 {result.LatestVersion}；自动启动使用固定版本 {DshPackageMetadata.ValidatedVersion}。",
    };
    public string CheckedAt => UpdateResult is null ? "-" : UpdateResult.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss");

    partial void OnDiagnosticsChanged(DependencyDiagnosticsResult value)
    {
        OnPropertyChanged(nameof(DesktopVersion));
        OnPropertyChanged(nameof(DotNetVersion));
        OnPropertyChanged(nameof(WebView2RuntimeVersion));
        OnPropertyChanged(nameof(NodeVersion));
        OnPropertyChanged(nameof(NpxPath));
        OnPropertyChanged(nameof(DshPath));
        OnPropertyChanged(nameof(DshVersion));
    }

    partial void OnUpdateResultChanged(DshUpdateCheckResult? value)
    {
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(UpdateStatus));
        OnPropertyChanged(nameof(CheckedAt));
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshDiagnosticsCommand.NotifyCanExecuteChanged();
        CheckUpdateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
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
            UpdateResult = await _releaseService.CheckLatestAsync(cancellationToken);
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
    }
}
