using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;

namespace DeepSeekHarnessDesktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IHarnessLifecycleCoordinator _coordinator;
    private readonly IHarnessHealthMonitor _healthMonitor;
    private readonly IUserConfirmationService _confirmation;

    [ObservableProperty]
    private string _serviceAddress;

    [ObservableProperty]
    private string _statusMessage = "仅接受本机 loopback HTTP 或 HTTPS 地址。";

    [ObservableProperty]
    private bool _isBusy;

    public SettingsViewModel(
        IHarnessLifecycleCoordinator coordinator,
        IHarnessHealthMonitor healthMonitor,
        IUserConfirmationService confirmation,
        AppSettings settings)
    {
        _coordinator = coordinator;
        _healthMonitor = healthMonitor;
        _confirmation = confirmation;
        _serviceAddress = settings.ServiceUri.AbsoluteUri;
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanRunCommand);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, CanApply);
        RestoreDefaultCommand = new RelayCommand(RestoreDefault, () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        coordinator.StateChanged += OnCoordinatorStateChanged;
    }

    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }
    public IRelayCommand RestoreDefaultCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public bool CanApplyServiceAddress => _coordinator.Current.State is
        HarnessRuntimeState.Stopped or
        HarnessRuntimeState.Failed or
        HarnessRuntimeState.RunningOwned or
        HarnessRuntimeState.RunningExternal;

    partial void OnServiceAddressChanged(string value) => ApplyCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        RestoreDefaultCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanRunCommand() => !IsBusy;

    private bool CanApply() => !IsBusy
        && CanApplyServiceAddress
        && ServiceUriValidator.TryNormalize(ServiceAddress, out _, out _);

    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!ServiceUriValidator.TryNormalize(ServiceAddress, out var uri, out var error))
        {
            StatusMessage = error;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _healthMonitor.ProbeAsync(uri, TimeSpan.FromSeconds(5), cancellationToken);
            StatusMessage = result.Status switch
            {
                HealthProbeStatus.DshConfirmed => $"连接成功，已确认 DeepSeek Harness：{result.FinalUri ?? result.RequestedUri}",
                HealthProbeStatus.Unreachable => "无法连接到该地址，请确认服务已启动。",
                HealthProbeStatus.ReachableUnknown => "地址可访问，但无法确认是 DeepSeek Harness。",
                HealthProbeStatus.ExternalRedirect => "服务重定向到不允许的地址。",
                _ => "服务地址无效。",
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (!ServiceUriValidator.TryNormalize(ServiceAddress, out var uri, out var error))
        {
            StatusMessage = error;
            return;
        }

        if (_coordinator.Current.State == HarnessRuntimeState.RunningOwned
            && !_confirmation.ConfirmServiceRestart(_coordinator.Current.ServiceUri ?? uri, uri))
        {
            StatusMessage = "未更改服务地址。";
            return;
        }

        IsBusy = true;
        try
        {
            await _coordinator.ApplyServiceUriAsync(uri, cancellationToken);
            ServiceAddress = uri.AbsoluteUri;
            StatusMessage = _coordinator.Current.State == HarnessRuntimeState.RunningOwned
                ? "服务地址已保存并完成重启。"
                : "服务地址已保存。";
        }
        catch (HarnessException exception)
        {
            StatusMessage = $"{exception.Error.Code} · {exception.Error.UserMessage}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RestoreDefault()
    {
        ServiceAddress = DshPackageMetadata.DefaultServiceUri.AbsoluteUri;
        StatusMessage = "已恢复默认输入，点击“应用”后保存。";
    }

    private void OnCoordinatorStateChanged(object? sender, HarnessStateSnapshot snapshot)
    {
        Dispatch(() =>
        {
            OnPropertyChanged(nameof(CanApplyServiceAddress));
            ApplyCommand.NotifyCanExecuteChanged();
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

    public void Cancel()
    {
        TestConnectionCommand.Cancel();
        ApplyCommand.Cancel();
    }

    public void Dispose()
    {
        Cancel();
        _coordinator.StateChanged -= OnCoordinatorStateChanged;
    }
}
