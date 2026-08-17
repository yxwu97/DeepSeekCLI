using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
using System.Collections.ObjectModel;

namespace DeepSeekHarnessDesktop.ViewModels;

public sealed partial class InstallationGuideViewModel : ObservableObject, IDisposable
{
    public const string ManualInstallCommandText =
        "npx " + DshPackageMetadata.ValidatedPackageSpec + " web";
    private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(1);
    private readonly IDependencyDiagnosticsService _diagnosticsService;
    private readonly IHarnessLifecycleCoordinator _coordinator;
    private readonly IRecentLogBuffer _logBuffer;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly IUserConfirmationService _confirmation;
    private readonly IClipboardService? _clipboard;
    private readonly ITerminalLauncher? _terminalLauncher;
    private readonly TimeProvider _timeProvider;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _timerCancellation;
    private long _operationStartedAt;
    private long _stageStartedAt;
    private bool _disposed;

    [ObservableProperty]
    private DependencyDiagnosticsResult _diagnostics;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _stageMessage = "检查本机启动条件，然后通过 npx 准备 DSH。";

    [ObservableProperty]
    private string _currentStage = "等待操作";

    [ObservableProperty]
    private string _elapsedText = "阶段 00:00 · 总计 00:00 / 05:00";

    public InstallationGuideViewModel(
        IDependencyDiagnosticsService diagnosticsService,
        IHarnessLifecycleCoordinator coordinator,
        IRecentLogBuffer logBuffer,
        IExternalLinkLauncher linkLauncher,
        IUserConfirmationService confirmation,
        DependencyDiagnosticsResult diagnostics,
        AppSettings? settings = null,
        IClipboardService? clipboard = null,
        ITerminalLauncher? terminalLauncher = null,
        TimeProvider? timeProvider = null)
    {
        _diagnosticsService = diagnosticsService;
        _coordinator = coordinator;
        _logBuffer = logBuffer;
        _linkLauncher = linkLauncher;
        _confirmation = confirmation;
        _diagnostics = diagnostics;
        _settings = settings ?? new AppSettings { WorkspacePath = Path.GetTempPath() };
        _clipboard = clipboard;
        _terminalLauncher = terminalLauncher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _elapsedText = $"阶段 00:00 · 总计 00:00 / {TimeoutText}";
        _isActive = !diagnostics.CanLaunchDsh;
        RecentLogs = new ObservableCollection<ProcessOutputLine>(logBuffer.Snapshot());
        RecheckCommand = new AsyncRelayCommand(RecheckAsync, () => !IsBusy);
        DownloadAndStartCommand = new AsyncRelayCommand(DownloadAndStartAsync, CanStart);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => IsBusy);
        OpenNodeDownloadCommand = new RelayCommand(() => OpenResource(OfficialResource.NodeDownload));
        OpenDocumentationCommand = new RelayCommand(() => OpenResource(OfficialResource.DshDocumentation));
        OpenNpmPackageCommand = new RelayCommand(() => OpenResource(OfficialResource.NpmPackage));
        CopyManualInstallCommand = new RelayCommand(CopyManualCommand, () => _clipboard is not null);
        CopyLogsCommand = new RelayCommand(CopyLogs, () => _clipboard is not null && RecentLogs.Count != 0);
        OpenPowerShellCommand = new RelayCommand(OpenPowerShell, () => _terminalLauncher is not null);
        CloseCommand = new RelayCommand(() => IsActive = false, () => !IsBusy);
        coordinator.StateChanged += OnStateChanged;
        logBuffer.LineAdded += OnLogLineAdded;
    }

    public ObservableCollection<ProcessOutputLine> RecentLogs { get; }
    public IAsyncRelayCommand RecheckCommand { get; }
    public IAsyncRelayCommand DownloadAndStartCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand OpenNodeDownloadCommand { get; }
    public IRelayCommand OpenDocumentationCommand { get; }
    public IRelayCommand OpenNpmPackageCommand { get; }
    public IRelayCommand CopyManualInstallCommand { get; }
    public IRelayCommand CopyLogsCommand { get; }
    public IRelayCommand OpenPowerShellCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public bool HasGlobalDsh => Diagnostics.GlobalDsh.Status == DependencyStatus.Available;
    public bool HasNode => Diagnostics.Node.Status == DependencyStatus.Available;
    public bool HasNpx => Diagnostics.Npx.Status == DependencyStatus.Available;
    public bool CanLaunch => Diagnostics.CanLaunchDsh;
    public string NodeStatusText => FormatCheck(Diagnostics.Node);
    public string NpxStatusText => FormatCheck(Diagnostics.Npx);
    public string DshStatusText => HasGlobalDsh
        ? $"全局可用 · {Diagnostics.GlobalDsh.Version ?? "版本未知"}"
        : $"npx 自动准备已验证版本 · {DshPackageMetadata.ValidatedVersion}";
    public string ManualInstallCommand => ManualInstallCommandText;

    public void Activate()
    {
        IsActive = true;
        StageMessage = CanLaunch
            ? "启动条件已满足，可以准备并启动 DSH。"
            : "请先安装 Node.js，再重新检查；也可以展开手动安装步骤。";
        LogDiagnostics("打开安装引导");
    }

    partial void OnDiagnosticsChanged(DependencyDiagnosticsResult value)
    {
        OnPropertyChanged(nameof(HasGlobalDsh));
        OnPropertyChanged(nameof(HasNode));
        OnPropertyChanged(nameof(HasNpx));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(NodeStatusText));
        OnPropertyChanged(nameof(NpxStatusText));
        OnPropertyChanged(nameof(DshStatusText));
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
        BeginTiming("检查环境");
        StageMessage = "正在重新读取系统和用户 PATH 并检查启动条件...";
        _logBuffer.AddDesktop("开始重新检查 WebView2、全局 DSH、Node.js 和 npx。");
        try
        {
            Diagnostics = await _diagnosticsService.DiagnoseAsync(cancellationToken);
            LogDiagnostics("环境检查完成");
            StageMessage = CanLaunch
                ? "启动条件已满足。"
                : "仍缺少可用的 Node.js/npx 或全局 DSH；安装后可重新检查。";
            FinishTiming(CanLaunch ? "环境检查通过" : "环境检查未通过");
        }
        catch (OperationCanceledException)
        {
            StageMessage = "环境检查已取消。";
            FinishTiming("环境检查已取消");
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
            StageMessage = "已取消准备 DSH。";
            _logBuffer.AddDesktop("用户取消了 npx 准备操作。");
            return;
        }

        IsBusy = true;
        BeginTiming(HasGlobalDsh ? "启动全局 DSH" : "准备 npm 包");
        StageMessage = HasGlobalDsh
            ? "正在启动全局 DSH..."
            : $"正在通过 npx 准备已验证的 DSH {DshPackageMetadata.ValidatedVersion}，最长等待 {TimeoutText}...";
        LogPreparation();
        try
        {
            await _coordinator.StartAsync(cancellationToken);
            CompleteFromSnapshot(_coordinator.Current);
        }
        catch (OperationCanceledException)
        {
            StageMessage = "启动已取消。";
            FinishTiming("启动已取消");
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
        _logBuffer.AddDesktop("用户请求取消当前安装或启动操作。");
        if (_coordinator.Current.State == HarnessRuntimeState.Starting)
        {
            await _coordinator.StopAsync(CancellationToken.None);
        }
        StageMessage = "启动已取消。";
        FinishTiming("启动已取消");
    }

    private void CompleteFromSnapshot(HarnessStateSnapshot snapshot)
    {
        if (snapshot.State is HarnessRuntimeState.RunningOwned or HarnessRuntimeState.RunningExternal)
        {
            StageMessage = "DeepSeek Harness 已就绪。";
            FinishTiming($"DSH 已就绪，PID {snapshot.ProcessId?.ToString() ?? "外部"}");
            IsActive = false;
            return;
        }

        if (snapshot.State == HarnessRuntimeState.Failed)
        {
            StageMessage = $"{snapshot.Error?.Code} · {snapshot.Error?.UserMessage}";
            FinishTiming($"启动失败：{snapshot.Error?.Code ?? "未知错误"}");
        }
    }

    private void OnStateChanged(object? sender, HarnessStateSnapshot snapshot)
    {
        Dispatch(() =>
        {
            if (snapshot.State is HarnessRuntimeState.Starting or HarnessRuntimeState.Restarting)
            {
                ChangeStage(snapshot.StatusMessage);
                StageMessage = snapshot.StatusMessage;
            }
            else if (snapshot.State == HarnessRuntimeState.Stopping && IsBusy)
            {
                ChangeStage("回收进程树");
                StageMessage = snapshot.StatusMessage;
            }
            else
            {
                CompleteFromSnapshot(snapshot);
            }

            if (snapshot.Error?.Code == "DSH-E101")
            {
                IsActive = true;
            }
        });
    }

    private void LogPreparation()
    {
        LogDiagnostics("开始准备");
        var command = HasGlobalDsh
            ? "dsh web"
            : $"npx -y {DshPackageMetadata.ValidatedPackageSpec} web";
        _logBuffer.AddDesktop(
            $"计划命令：{command}；工作目录：{_settings.WorkspacePath}；"
            + $"目标地址：{_settings.ServiceUri}；最长等待：{TimeoutText}。");
    }

    private void LogDiagnostics(string prefix)
    {
        _logBuffer.AddDesktop(
            $"{prefix}：WebView2={Diagnostics.WebView2.Status}；"
            + $"全局 DSH={Diagnostics.GlobalDsh.Status}；Node.js={Diagnostics.Node.Status}"
            + $"{FormatVersion(Diagnostics.Node.Version)}；npx={Diagnostics.Npx.Status}。");
    }

    private static string FormatCheck(DependencyCheck check) => check.Status switch
    {
        DependencyStatus.Available => check.Version is null ? "可用" : $"可用 · {check.Version}",
        DependencyStatus.Unusable => "存在但无法运行",
        _ => "未检测到",
    };

    private static string FormatVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? string.Empty : $" ({version})";

    private void BeginTiming(string stage)
    {
        StopTimer();
        _operationStartedAt = _timeProvider.GetTimestamp();
        _stageStartedAt = _operationStartedAt;
        CurrentStage = stage;
        _timerCancellation = new CancellationTokenSource();
        UpdateElapsed();
        _ = RunTimerAsync(_timerCancellation.Token);
        _logBuffer.AddDesktop($"开始阶段：{stage}。");
    }

    private void ChangeStage(string stage)
    {
        if (_timerCancellation is null || string.Equals(CurrentStage, stage, StringComparison.Ordinal))
        {
            return;
        }

        var elapsed = _timeProvider.GetElapsedTime(_stageStartedAt);
        _logBuffer.AddDesktop($"阶段完成：{CurrentStage}，耗时 {FormatElapsed(elapsed)}。");
        CurrentStage = stage;
        _stageStartedAt = _timeProvider.GetTimestamp();
        UpdateElapsed();
        _logBuffer.AddDesktop($"开始阶段：{stage}。");
    }

    private void FinishTiming(string result)
    {
        if (_timerCancellation is null)
        {
            return;
        }

        UpdateElapsed();
        var stageElapsed = _timeProvider.GetElapsedTime(_stageStartedAt);
        var totalElapsed = _timeProvider.GetElapsedTime(_operationStartedAt);
        _logBuffer.AddDesktop($"阶段完成：{CurrentStage}，耗时 {FormatElapsed(stageElapsed)}。");
        _logBuffer.AddDesktop($"{result}；总耗时 {FormatElapsed(totalElapsed)}。");
        StopTimer();
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimerInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Dispatch(UpdateElapsed);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateElapsed()
    {
        if (_timerCancellation is null || _disposed)
        {
            return;
        }

        var stage = _timeProvider.GetElapsedTime(_stageStartedAt);
        var total = _timeProvider.GetElapsedTime(_operationStartedAt);
        ElapsedText = $"阶段 {FormatElapsed(stage)} · 总计 {FormatElapsed(total)} / {TimeoutText}";
    }

    private string TimeoutText => FormatElapsed(TimeSpan.FromSeconds(_settings.StartupTimeoutSeconds));

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

    private void StopTimer()
    {
        var cancellation = Interlocked.Exchange(ref _timerCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void CopyManualCommand() => RunManualAction(
        () => _clipboard!.SetText(ManualInstallCommandText),
        "手动安装命令已复制。",
        "复制手动安装命令");

    private void CopyLogs()
    {
        var text = string.Join(Environment.NewLine, _logBuffer.Snapshot().Select(line => line.DisplayText));
        RunManualAction(() => _clipboard!.SetText(text), "日志已复制。", "复制安装日志");
    }

    private void OpenPowerShell() => RunManualAction(
        () => _terminalLauncher!.OpenPowerShell(_settings.WorkspacePath),
        "已在工作目录打开 PowerShell，请粘贴手动命令。",
        "打开 PowerShell");

    private void OpenResource(OfficialResource resource) => RunManualAction(
        () => _linkLauncher.Open(resource),
        "已在系统浏览器打开官方页面。",
        $"打开官方资源 {resource}");

    private void RunManualAction(Action action, string successMessage, string logDescription)
    {
        try
        {
            action();
            StageMessage = successMessage;
            _logBuffer.AddDesktop($"{logDescription}成功。");
        }
        catch (Exception exception)
        {
            var error = exception is HarnessException harness ? harness.Error : null;
            StageMessage = error?.UserMessage ?? "操作失败，请稍后重试。";
            _logBuffer.AddDesktop($"{logDescription}失败：{error?.Code ?? exception.GetType().Name}。");
        }
    }

    private void OnLogLineAdded(object? sender, ProcessOutputLine line)
    {
        Dispatch(() =>
        {
            if (RecentLogs.Count == RecentLogBuffer.Capacity)
            {
                RecentLogs.RemoveAt(0);
            }
            RecentLogs.Add(line);
            CopyLogsCommand.NotifyCanExecuteChanged();
        });
    }

    private void Dispatch(Action action)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.BeginInvoke(action);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTimer();
        RecheckCommand.Cancel();
        DownloadAndStartCommand.Cancel();
        _coordinator.StateChanged -= OnStateChanged;
        _logBuffer.LineAdded -= OnLogLineAdded;
    }
}
