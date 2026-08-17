using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Collections.ObjectModel;
using System.Windows;

namespace DeepSeekHarnessDesktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IHarnessLifecycleCoordinator _coordinator;
    private readonly ICodeWebViewService _codeWebView;
    private readonly IChatWebViewService? _chatWebView;
    private readonly IUserConfirmationService? _confirmation;
    private readonly IWorkspacePicker _workspacePicker;
    private readonly IRecentLogBuffer _recentLogBuffer;
    private readonly AppSettings _settings;
    private readonly string _desktopVersion;
    private Uri? _lastNavigatedUri;
    private bool _chatInitializationRequested;
    private AppContentMode _currentMode = AppContentMode.Code;
    private ChatPageSnapshot _chatSnapshot = ChatPageSnapshot.Initial;

    [ObservableProperty]
    private HarnessStateSnapshot _snapshot;

    [ObservableProperty]
    private string _workspacePath;

    public MainWindowViewModel(
        IHarnessLifecycleCoordinator coordinator,
        ICodeWebViewService codeWebView,
        IWorkspacePicker workspacePicker,
        IRecentLogBuffer recentLogBuffer,
        AppSettings settings,
        DependencyDiagnosticsResult diagnostics,
        InstallationGuideViewModel? installationGuide = null,
        IChatWebViewService? chatWebView = null,
        IUserConfirmationService? confirmation = null)
    {
        _coordinator = coordinator;
        _codeWebView = codeWebView;
        _chatWebView = chatWebView;
        _confirmation = confirmation;
        _workspacePicker = workspacePicker;
        _recentLogBuffer = recentLogBuffer;
        _settings = settings;
        _desktopVersion = diagnostics.DesktopVersion;
        InstallationGuide = installationGuide;
        _snapshot = coordinator.Current;
        _workspacePath = settings.WorkspacePath;
        RecentLogs = new ObservableCollection<ProcessOutputLine>(recentLogBuffer.Snapshot());
        coordinator.StateChanged += OnStateChanged;
        recentLogBuffer.LineAdded += OnLogLineAdded;
        if (installationGuide is not null)
        {
            installationGuide.PropertyChanged += OnInstallationGuidePropertyChanged;
        }
        if (chatWebView is not null)
        {
            _chatSnapshot = chatWebView.Current;
            chatWebView.StateChanged += OnChatStateChanged;
        }

        StartCommand = new AsyncRelayCommand(
            () => coordinator.StartAsync(CancellationToken.None),
            () => IsCodeMode && State is HarnessRuntimeState.Stopped or HarnessRuntimeState.Failed);
        StopCommand = new AsyncRelayCommand(
            () => coordinator.StopAsync(CancellationToken.None),
            () => IsCodeMode && State is HarnessRuntimeState.Starting or HarnessRuntimeState.RunningOwned);
        RestartCommand = new AsyncRelayCommand(
            () => coordinator.RestartAsync(CancellationToken.None),
            () => IsCodeMode && State == HarnessRuntimeState.RunningOwned);
        ReloadPageCommand = new AsyncRelayCommand(
            ReloadCurrentPageAsync,
            () => CanReloadPage);
        SwitchToCodeCommand = new RelayCommand(
            () => CurrentMode = AppContentMode.Code,
            () => !IsCodeMode);
        SwitchToChatCommand = new AsyncRelayCommand(
            SwitchToChatAsync,
            () => !IsChatMode);
        RetryChatCommand = new AsyncRelayCommand(
            () => _chatWebView?.ReloadAsync(CancellationToken.None) ?? Task.CompletedTask,
            () => IsChatMode && ChatSnapshot.State == ChatPageState.Failed);
        ClearChatDataCommand = new AsyncRelayCommand(
            ClearChatDataAsync,
            () => IsChatMode
                && _chatWebView?.IsInitialized == true
                && ChatSnapshot.State is ChatPageState.Ready or ChatPageState.Failed);
        SelectWorkspaceCommand = new RelayCommand(SelectWorkspace, () => CanChangeWorkspace);
        OpenLogsCommand = new RelayCommand(() => OpenLogsRequested?.Invoke(this, EventArgs.Empty));
        OpenInstallationGuideCommand = new RelayCommand(() => InstallationGuide?.Activate());
    }

    public event EventHandler? OpenLogsRequested;

    public AppContentMode CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (SetProperty(ref _currentMode, value))
            {
                NotifyModeChanged();
            }
        }
    }

    public ChatPageSnapshot ChatSnapshot
    {
        get => _chatSnapshot;
        private set => SetProperty(ref _chatSnapshot, value);
    }

    public HarnessRuntimeState State => Snapshot.State;
    public string StatusTitle => IsChatMode ? ChatSnapshot.State switch
    {
        ChatPageState.Initializing => "正在加载 DeepSeek Chat",
        ChatPageState.Ready => "DeepSeek Chat",
        ChatPageState.Failed => "DeepSeek Chat 加载失败",
        ChatPageState.ClearingData => "正在清除 Chat 登录信息",
        _ => "DeepSeek Chat",
    } : State switch
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
    public string StatusDetail => IsChatMode
        ? ChatSnapshot.Error is null
            ? ChatSnapshot.StatusMessage
            : $"{ChatSnapshot.Error.Code} · {ChatSnapshot.Error.UserMessage}"
        : Snapshot.Error is null
        ? Snapshot.StatusMessage
        : $"{Snapshot.Error.Code} · {Snapshot.Error.UserMessage}";
    public Uri? ServiceUri => Snapshot.ServiceUri;
    public bool IsRunning => State is HarnessRuntimeState.RunningOwned or HarnessRuntimeState.RunningExternal;
    public bool IsCodeMode => CurrentMode == AppContentMode.Code;
    public bool IsChatMode => CurrentMode == AppContentMode.Chat;
    public bool IsInstallationGuideActive => InstallationGuide?.IsActive == true;
    public bool IsCodeWebViewVisible => IsCodeMode && IsRunning && !IsInstallationGuideActive;
    public bool IsChatWebViewVisible => IsChatMode && ChatSnapshot.State == ChatPageState.Ready;
    public bool IsWebViewVisible => IsCodeWebViewVisible;
    public bool CanChangeWorkspace => IsCodeMode && State is HarnessRuntimeState.Stopped or HarnessRuntimeState.Failed;
    public bool CanReloadPage => IsCodeMode ? IsRunning : ChatSnapshot.State is ChatPageState.Ready or ChatPageState.Failed;
    public string DesktopVersion => $"MVP {_desktopVersion}";
    public ObservableCollection<ProcessOutputLine> RecentLogs { get; }
    public InstallationGuideViewModel? InstallationGuide { get; }

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IAsyncRelayCommand RestartCommand { get; }
    public IAsyncRelayCommand ReloadPageCommand { get; }
    public IRelayCommand SwitchToCodeCommand { get; }
    public IAsyncRelayCommand SwitchToChatCommand { get; }
    public IAsyncRelayCommand RetryChatCommand { get; }
    public IAsyncRelayCommand ClearChatDataCommand { get; }
    public IRelayCommand SelectWorkspaceCommand { get; }
    public IRelayCommand OpenLogsCommand { get; }
    public IRelayCommand OpenInstallationGuideCommand { get; }

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

    private async Task SwitchToChatAsync()
    {
        CurrentMode = AppContentMode.Chat;
        if (_chatWebView is not null && !_chatInitializationRequested)
        {
            _chatInitializationRequested = true;
            await _chatWebView.InitializeAsync(CancellationToken.None);
        }
    }

    private Task ReloadCurrentPageAsync() => IsCodeMode
        ? _codeWebView.ReloadAsync(CancellationToken.None)
        : _chatWebView?.ReloadAsync(CancellationToken.None) ?? Task.CompletedTask;

    private async Task ClearChatDataAsync()
    {
        if (_chatWebView is null || _confirmation?.ConfirmClearChatData() != true)
        {
            return;
        }
        await _chatWebView.ClearBrowsingDataAsync(CancellationToken.None);
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
        OnPropertyChanged(nameof(IsCodeWebViewVisible));
        OnPropertyChanged(nameof(IsWebViewVisible));
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

    private void OnInstallationGuidePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallationGuideViewModel.IsActive))
        {
            OnPropertyChanged(nameof(IsInstallationGuideActive));
            OnPropertyChanged(nameof(IsCodeWebViewVisible));
            OnPropertyChanged(nameof(IsWebViewVisible));
        }
    }

    private async Task NavigateAsync(Uri uri)
    {
        try
        {
            await _codeWebView.NavigateAsync(uri, CancellationToken.None);
        }
        catch (HarnessException exception)
        {
            _recentLogBuffer.Add(new ProcessOutputLine(
                DateTimeOffset.UtcNow,
                ProcessOutputSource.StandardError,
                $"{exception.Error.Code}: {exception.Error.TechnicalMessage}"));
        }
    }

    private void OnChatStateChanged(object? sender, ChatPageSnapshot snapshot)
    {
        Dispatch(() =>
        {
            ChatSnapshot = snapshot;
            OnPropertyChanged(nameof(IsChatWebViewVisible));
            OnPropertyChanged(nameof(StatusTitle));
            OnPropertyChanged(nameof(StatusDetail));
            OnPropertyChanged(nameof(CanReloadPage));
            ReloadPageCommand.NotifyCanExecuteChanged();
            RetryChatCommand.NotifyCanExecuteChanged();
            ClearChatDataCommand.NotifyCanExecuteChanged();
        });
    }

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(IsCodeMode));
        OnPropertyChanged(nameof(IsChatMode));
        OnPropertyChanged(nameof(IsCodeWebViewVisible));
        OnPropertyChanged(nameof(IsChatWebViewVisible));
        OnPropertyChanged(nameof(IsWebViewVisible));
        OnPropertyChanged(nameof(CanChangeWorkspace));
        OnPropertyChanged(nameof(CanReloadPage));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusDetail));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        ReloadPageCommand.NotifyCanExecuteChanged();
        SelectWorkspaceCommand.NotifyCanExecuteChanged();
        SwitchToCodeCommand.NotifyCanExecuteChanged();
        SwitchToChatCommand.NotifyCanExecuteChanged();
        RetryChatCommand.NotifyCanExecuteChanged();
        ClearChatDataCommand.NotifyCanExecuteChanged();
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

    public void Dispose()
    {
        _coordinator.StateChanged -= OnStateChanged;
        _recentLogBuffer.LineAdded -= OnLogLineAdded;
        if (InstallationGuide is not null)
        {
            InstallationGuide.PropertyChanged -= OnInstallationGuidePropertyChanged;
        }
        if (_chatWebView is not null)
        {
            _chatWebView.StateChanged -= OnChatStateChanged;
        }
    }
}
