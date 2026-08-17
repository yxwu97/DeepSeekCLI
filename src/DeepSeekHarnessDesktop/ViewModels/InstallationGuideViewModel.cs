using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Collections.ObjectModel;

namespace DeepSeekHarnessDesktop.ViewModels;

public sealed partial class InstallationGuideViewModel : ObservableObject, IDisposable
{
    private const int VisibleLogCapacity = 200;
    private readonly IDependencyDiagnosticsService _diagnosticsService;
    private readonly IHarnessLifecycleCoordinator _coordinator;
    private readonly IRecentLogBuffer _logBuffer;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly IUserConfirmationService _confirmation;

    [ObservableProperty]
    private DependencyDiagnosticsResult _diagnostics;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _stageMessage = "检查本机启动条件，然后下载并启动已验证的固定版本。";

    public InstallationGuideViewModel(
        IDependencyDiagnosticsService diagnosticsService,
        IHarnessLifecycleCoordinator coordinator,
        IRecentLogBuffer logBuffer,
        IExternalLinkLauncher linkLauncher,
        IUserConfirmationService confirmation,
        DependencyDiagnosticsResult diagnostics)
    {
        _diagnosticsService = diagnosticsService;
        _coordinator = coordinator;
        _logBuffer = logBuffer;
        _linkLauncher = linkLauncher;
        _confirmation = confirmation;
        _diagnostics = diagnostics;
        _isActive = !diagnostics.CanLaunchDsh;
        RecentLogs = new ObservableCollection<ProcessOutputLine>(logBuffer.Snapshot().TakeLast(VisibleLogCapacity));
        RecheckCommand = new AsyncRelayCommand(RecheckAsync, () => !IsBusy);
        DownloadAndStartCommand = new AsyncRelayCommand(DownloadAndStartAsync, CanStart);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => IsBusy);
        OpenNodeDownloadCommand = new RelayCommand(() => linkLauncher.Open(OfficialResource.NodeDownload));
        CloseCommand = new RelayCommand(() => IsActive = false, () => !IsBusy);
        coordinator.StateChanged += OnStateChanged;
        logBuffer.LineAdded += OnLogLineAdded;
    }

    public ObservableCollection<ProcessOutputLine> RecentLogs { get; }
    public IAsyncRelayCommand RecheckCommand { get; }
    public IAsyncRelayCommand DownloadAndStartCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand OpenNodeDownloadCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public bool HasGlobalDsh => Diagnostics.GlobalDsh.Status == DependencyStatus.Available;
    public bool HasNode => Diagnostics.Node.Status == DependencyStatus.Available;
    public bool HasNpx => Diagnostics.Npx.Status == DependencyStatus.Available;
    public bool CanLaunch => Diagnostics.CanLaunchDsh;
    public string NodeStatusText => HasNode ? "可用" : "未检测到";
    public string NpxStatusText => HasNpx ? "可用" : "未检测到";

    public void Activate()
    {
        IsActive = true;
        StageMessage = CanLaunch
            ? "启动条件已满足，可以启动固定版本 DSH。"
            : "请先安装 Node.js，并确认 node 与 npx 已加入 PATH。";
    }

    partial void OnDiagnosticsChanged(DependencyDiagnosticsResult value)
    {
        OnPropertyChanged(nameof(HasGlobalDsh));
        OnPropertyChanged(nameof(HasNode));
        OnPropertyChanged(nameof(HasNpx));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(NodeStatusText));
        OnPropertyChanged(nameof(NpxStatusText));
        DownloadAndStartCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RecheckCommand.NotifyCanExecuteChanged();
        DownloadAndStartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsBusy && CanLaunch;

    private async Task RecheckAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StageMessage = "正在重新检查启动条件...";
        try
        {
            Diagnostics = await _diagnosticsService.DiagnoseAsync(cancellationToken);
            StageMessage = CanLaunch
                ? "启动条件已满足。"
                : "仍缺少可用的 Node.js/npx 或全局 DSH。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DownloadAndStartAsync(CancellationToken cancellationToken)
    {
        if (!HasGlobalDsh && !_confirmation.ConfirmDshDownload())
        {
            StageMessage = "已取消下载。";
            return;
        }

        IsBusy = true;
        StageMessage = HasGlobalDsh ? "正在启动全局 DSH..." : "正在通过 npx 准备并启动固定版本 DSH...";
        try
        {
            await _coordinator.StartAsync(cancellationToken);
            if (_coordinator.Current.State == HarnessRuntimeState.Failed)
            {
                StageMessage = $"{_coordinator.Current.Error?.Code} · {_coordinator.Current.Error?.UserMessage}";
            }
        }
        catch (OperationCanceledException)
        {
            StageMessage = "启动已取消。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CancelAsync()
    {
        RecheckCommand.Cancel();
        DownloadAndStartCommand.Cancel();
        if (_coordinator.Current.State == HarnessRuntimeState.Starting)
        {
            await _coordinator.StopAsync(CancellationToken.None);
        }
        StageMessage = "启动已取消。";
    }

    private void OnStateChanged(object? sender, HarnessStateSnapshot snapshot)
    {
        Dispatch(() =>
        {
            if (snapshot.State is HarnessRuntimeState.RunningOwned or HarnessRuntimeState.RunningExternal)
            {
                IsActive = false;
                StageMessage = "DeepSeek Harness 已就绪。";
            }
            else if (snapshot.Error?.Code == "DSH-E101")
            {
                IsActive = true;
                StageMessage = snapshot.Error.UserMessage;
            }
            else if (IsActive && snapshot.State is HarnessRuntimeState.Starting or HarnessRuntimeState.Stopping)
            {
                StageMessage = snapshot.StatusMessage;
            }
        });
    }

    private void OnLogLineAdded(object? sender, ProcessOutputLine line)
    {
        Dispatch(() =>
        {
            if (RecentLogs.Count == VisibleLogCapacity)
            {
                RecentLogs.RemoveAt(0);
            }
            RecentLogs.Add(line);
        });
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    public void Dispose()
    {
        RecheckCommand.Cancel();
        DownloadAndStartCommand.Cancel();
        _coordinator.StateChanged -= OnStateChanged;
        _logBuffer.LineAdded -= OnLogLineAdded;
    }
}
