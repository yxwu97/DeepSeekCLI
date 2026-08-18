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
    private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(1);
    private readonly IDependencyDiagnosticsService _diagnosticsService;
    private readonly IHarnessLifecycleCoordinator _coordinator;
    private readonly IRecentLogBuffer _logBuffer;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly IUserConfirmationService _confirmation;
    private readonly IClipboardService? _clipboard;
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
    private string _stageMessage = "正在检查 WebView2、Node.js 和 DSH。";

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
        _timeProvider = timeProvider ?? TimeProvider.System;
        _elapsedText = $"阶段 00:00 · 总计 00:00 / {TimeoutText}";
        _isActive = NeedsGuidedPreparation(diagnostics);
        RecentLogs = new ObservableCollection<ProcessOutputLine>(logBuffer.Snapshot());
        RecheckCommand = new AsyncRelayCommand(RecheckAsync, () => !IsBusy);
        DownloadAndStartCommand = new AsyncRelayCommand(ContinueAsync, () => !IsBusy);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => IsBusy);
        OpenDocumentationCommand = new RelayCommand(() => OpenResource(OfficialResource.DshDocumentation));
        CopyLogsCommand = new RelayCommand(CopyLogs, () => _clipboard is not null && RecentLogs.Count != 0);
        CloseCommand = new RelayCommand(() => IsActive = false, () => !IsBusy);
        coordinator.StateChanged += OnStateChanged;
        logBuffer.LineAdded += OnLogLineAdded;
    }

    public ObservableCollection<ProcessOutputLine> RecentLogs { get; }
    public IAsyncRelayCommand RecheckCommand { get; }
    public IAsyncRelayCommand DownloadAndStartCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand OpenDocumentationCommand { get; }
    public IRelayCommand CopyLogsCommand { get; }
    public IRelayCommand CloseCommand { get; }
    public bool HasWebView2 => Diagnostics.WebView2.Status == DependencyStatus.Available;
    public bool HasInstalledDsh => Diagnostics.GlobalDsh.Status == DependencyStatus.Available;
    public bool HasNode => Diagnostics.Node.Status == DependencyStatus.Available;
    public bool HasNpx => Diagnostics.Npx.Status == DependencyStatus.Available;
    public bool CanLaunch => HasWebView2
        && (_settings.Launch.Mode == LaunchMode.Custom || Diagnostics.CanLaunchDsh);
    public string WebView2StatusText => FormatCheck(Diagnostics.WebView2);
    public string NodeStatusText => FormatCheck(Diagnostics.Node);
    public string NpxStatusText => FormatCheck(Diagnostics.Npx);
    public string DshStatusText => HasInstalledDsh
        ? $"已安装 · {Diagnostics.GlobalDsh.Version ?? "版本未知"}"
        : HasNode && HasNpx
            ? $"将按需下载固定版本 {DshPackageMetadata.ValidatedVersion}"
            : "等待 Node.js 和 npx";
    public string PrimaryActionText => !HasWebView2
        ? "安装 WebView2"
        : _settings.Launch.Mode == LaunchMode.Auto && !Diagnostics.CanLaunchDsh
            ? "安装 Node.js"
            : "准备并启动";

    public void Activate()
    {
        IsActive = true;
        StageMessage = CanLaunch
            ? "环境检查通过，可以启动 DSH。"
            : "请按当前按钮完成缺失项，安装后点击“重新检查”。";
        LogDiagnostics("打开安装引导");
    }

    partial void OnDiagnosticsChanged(DependencyDiagnosticsResult value)
    {
        OnPropertyChanged(nameof(HasWebView2));
        OnPropertyChanged(nameof(HasInstalledDsh));
        OnPropertyChanged(nameof(HasNode));
        OnPropertyChanged(nameof(HasNpx));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(WebView2StatusText));
        OnPropertyChanged(nameof(NodeStatusText));
        OnPropertyChanged(nameof(NpxStatusText));
        OnPropertyChanged(nameof(DshStatusText));
        OnPropertyChanged(nameof(PrimaryActionText));
        if (_coordinator.Current.State is not (HarnessRuntimeState.RunningOwned or HarnessRuntimeState.RunningExternal))
        {
            IsActive = NeedsGuidedPreparation(value);
        }
        DownloadAndStartCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RecheckCommand.NotifyCanExecuteChanged();
        DownloadAndStartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }

    private async Task RecheckAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        BeginTiming("检查环境");
        StageMessage = "正在重新读取系统和用户 PATH，并检查 WebView2、Node.js、npx 和 DSH...";
        _logBuffer.AddDesktop("开始重新检查 WebView2、Node.js、npx 和 DSH。");
        try
        {
            Diagnostics = await _diagnosticsService.DiagnoseAsync(cancellationToken);
            LogDiagnostics("环境检查完成");
            StageMessage = CanLaunch
                ? "启动条件已满足。"
                : "仍有缺失项，请按当前按钮完成安装后重新检查。";
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

    private async Task ContinueAsync(CancellationToken cancellationToken)
    {
        if (!HasWebView2)
        {
            OpenResource(OfficialResource.WebView2Download);
            StageMessage = "请安装 WebView2 Runtime，完成后返回并点击“重新检查”。";
            return;
        }
        if (_settings.Launch.Mode == LaunchMode.Auto && !Diagnostics.CanLaunchDsh)
        {
            OpenResource(OfficialResource.NodeDownload);
            StageMessage = "请安装 Node.js LTS x64，完成后重新启动应用或点击“重新检查”。";
            return;
        }
        if (_settings.Launch.Mode == LaunchMode.Auto
            && !HasInstalledDsh
            && !_confirmation.ConfirmDshDownload())
        {
            StageMessage = "已取消下载 DSH。";
            _logBuffer.AddDesktop("用户取消了 npx 下载操作。");
            return;
        }

        IsBusy = true;
        BeginTiming(HasInstalledDsh ? "启动 DSH" : "下载并启动 DSH");
        StageMessage = HasInstalledDsh
            ? "正在启动已安装的 DSH..."
            : $"正在通过 npx 下载并启动 DSH {DshPackageMetadata.ValidatedVersion}，最长等待 {TimeoutText}...";
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
        var command = HasInstalledDsh
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
            + $"DSH={Diagnostics.GlobalDsh.Status}；Node.js={Diagnostics.Node.Status}"
            + $"{FormatVersion(Diagnostics.Node.Version)}；npx={Diagnostics.Npx.Status}。" );
    }

    private bool NeedsGuidedPreparation(DependencyDiagnosticsResult diagnostics) =>
        diagnostics.WebView2.Status != DependencyStatus.Available
        || (_settings.Launch.Mode == LaunchMode.Auto
            && diagnostics.GlobalDsh.Status != DependencyStatus.Available);

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

    private void CopyLogs()
    {
        var text = string.Join(Environment.NewLine, _logBuffer.Snapshot().Select(line => line.DisplayText));
        RunManualAction(() => _clipboard!.SetText(text), "日志已复制。", "复制安装日志");
    }

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
