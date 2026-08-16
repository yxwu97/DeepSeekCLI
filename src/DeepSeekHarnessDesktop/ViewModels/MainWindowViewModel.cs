using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Collections.ObjectModel;
using System.Windows;

namespace DeepSeekHarnessDesktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IHarnessLifecycleCoordinator _coordinator;
    private readonly IWebViewNavigationService _navigation;
    private readonly IWorkspacePicker _workspacePicker;
    private readonly IRecentLogBuffer _recentLogBuffer;
    private readonly AppSettings _settings;
    private readonly string _desktopVersion;
    private Uri? _lastNavigatedUri;

    [ObservableProperty]
    private HarnessStateSnapshot _snapshot;

    [ObservableProperty]
    private string _workspacePath;

    public MainWindowViewModel(
        IHarnessLifecycleCoordinator coordinator,
        IWebViewNavigationService navigation,
        IWorkspacePicker workspacePicker,
        IRecentLogBuffer recentLogBuffer,
        AppSettings settings,
        DependencyDiagnosticsResult diagnostics)
    {
        _coordinator = coordinator;
        _navigation = navigation;
        _workspacePicker = workspacePicker;
        _recentLogBuffer = recentLogBuffer;
        _settings = settings;
        _desktopVersion = diagnostics.DesktopVersion;
        _snapshot = coordinator.Current;
        _workspacePath = settings.WorkspacePath;
        RecentLogs = new ObservableCollection<ProcessOutputLine>(recentLogBuffer.Snapshot());
        coordinator.StateChanged += OnStateChanged;
        recentLogBuffer.LineAdded += OnLogLineAdded;

        StartCommand = new AsyncRelayCommand(
            () => coordinator.StartAsync(CancellationToken.None),
            () => State is HarnessRuntimeState.Stopped or HarnessRuntimeState.Failed);
        StopCommand = new AsyncRelayCommand(
            () => coordinator.StopAsync(CancellationToken.None),
            () => State is HarnessRuntimeState.Starting or HarnessRuntimeState.RunningOwned);
        RestartCommand = new AsyncRelayCommand(
            () => coordinator.RestartAsync(CancellationToken.None),
            () => State == HarnessRuntimeState.RunningOwned);
        ReloadPageCommand = new AsyncRelayCommand(
            () => navigation.ReloadAsync(CancellationToken.None),
            () => IsRunning);
        SelectWorkspaceCommand = new RelayCommand(SelectWorkspace, () => CanChangeWorkspace);
        OpenLogsCommand = new RelayCommand(() => OpenLogsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? OpenLogsRequested;

    public HarnessRuntimeState State => Snapshot.State;
    public string StatusTitle => State switch
    {
        HarnessRuntimeState.Initializing => "正在初始化",
        HarnessRuntimeState.Starting => "正在启动 DeepSeek Harness",
        HarnessRuntimeState.RunningOwned => "DeepSeek Harness 正在运行",
        HarnessRuntimeState.RunningExternal => "外部 DeepSeek Harness 正在运行",
        HarnessRuntimeState.Stopping => "正在停止",
        HarnessRuntimeState.Restarting => "正在重启",
        HarnessRuntimeState.Failed => "启动失败",
        _ => "DeepSeek Harness 已停止",
    };
    public string StatusDetail => Snapshot.Error is null
        ? Snapshot.StatusMessage
        : $"{Snapshot.Error.Code} · {Snapshot.Error.UserMessage}";
    public Uri? ServiceUri => Snapshot.ServiceUri;
    public bool IsRunning => State is HarnessRuntimeState.RunningOwned or HarnessRuntimeState.RunningExternal;
    public bool CanChangeWorkspace => State is HarnessRuntimeState.Stopped or HarnessRuntimeState.Failed;
    public string DesktopVersion => $"MVP {_desktopVersion}";
    public ObservableCollection<ProcessOutputLine> RecentLogs { get; }

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IAsyncRelayCommand RestartCommand { get; }
    public IAsyncRelayCommand ReloadPageCommand { get; }
    public IRelayCommand SelectWorkspaceCommand { get; }
    public IRelayCommand OpenLogsCommand { get; }

    private void SelectWorkspace()
    {
        var selected = _workspacePicker.Pick(WorkspacePath);
        if (selected is null)
        {
            return;
        }
        WorkspacePath = selected;
        _settings.WorkspacePath = selected;
    }

    private void OnStateChanged(object? sender, HarnessStateSnapshot snapshot)
    {
        Dispatch(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(HarnessStateSnapshot snapshot)
    {
        Snapshot = snapshot;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(ServiceUri));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanChangeWorkspace));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        ReloadPageCommand.NotifyCanExecuteChanged();
        SelectWorkspaceCommand.NotifyCanExecuteChanged();

        if (IsRunning && snapshot.ServiceUri is { } uri && uri != _lastNavigatedUri)
        {
            _lastNavigatedUri = uri;
            _ = NavigateAsync(uri);
        }
        else if (!IsRunning)
        {
            _lastNavigatedUri = null;
        }
    }

    private async Task NavigateAsync(Uri uri)
    {
        try
        {
            await _navigation.NavigateAsync(uri, CancellationToken.None);
        }
        catch (HarnessException exception)
        {
            _recentLogBuffer.Add(new ProcessOutputLine(
                DateTimeOffset.UtcNow,
                ProcessOutputSource.StandardError,
                $"{exception.Error.Code}: {exception.Error.TechnicalMessage}"));
        }
    }

    private void OnLogLineAdded(object? sender, ProcessOutputLine line)
    {
        Dispatch(() =>
        {
            if (RecentLogs.Count == Services.RecentLogBuffer.Capacity)
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
}
