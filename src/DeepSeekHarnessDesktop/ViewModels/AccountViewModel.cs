using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Collections.ObjectModel;

namespace DeepSeekHarnessDesktop.ViewModels;

public sealed partial class AccountViewModel : ObservableObject
{
    private readonly IDeepSeekAccountService _accountService;
    private readonly IDeepSeekApiKeyProvider _apiKeyProvider;
    private readonly IExternalLinkLauncher _linkLauncher;
    private string? _apiKey;
    private bool _hasBalanceResult;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "尚未连接";

    [ObservableProperty]
    private string _lastUpdatedText = "尚未查询";

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _isAvailable;

    public AccountViewModel(
        IDeepSeekAccountService accountService,
        IDeepSeekApiKeyProvider apiKeyProvider,
        IExternalLinkLauncher linkLauncher)
    {
        _accountService = accountService;
        _apiKeyProvider = apiKeyProvider;
        _linkLauncher = linkLauncher;
        Balances = new ObservableCollection<DeepSeekBalanceInfo>();
        OpenTopUpCommand = new RelayCommand(OpenTopUp);
    }

    public ObservableCollection<DeepSeekBalanceInfo> Balances { get; }
    public IRelayCommand OpenTopUpCommand { get; }
    public bool HasApiKey => _apiKey is not null;
    public bool HasBalances => Balances.Count > 0;
    public bool HasError => ErrorText is not null;
    public string MaskedApiKey => _apiKey is null ? "未设置" : MaskApiKey(_apiKey);
    public string AvailabilityText => !_hasBalanceResult
        ? "尚未查询"
        : IsAvailable ? "API 调用可用" : "余额不足或 API 调用不可用";

    public async Task RefreshAsync(string? candidateApiKey, CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorText = null;
        OnPropertyChanged(nameof(HasError));
        StatusText = "正在查询";
        try
        {
            _apiKey = !string.IsNullOrWhiteSpace(candidateApiKey)
                ? candidateApiKey.Trim()
                : await _apiKeyProvider.GetCurrentAsync(cancellationToken);
            OnPropertyChanged(nameof(HasApiKey));
            OnPropertyChanged(nameof(MaskedApiKey));

            if (_apiKey is null)
            {
                SetError("API-E600", "未找到 DeepSeek API Key，请先在 Harness 模型设置中配置");
                return;
            }

            var snapshot = await _accountService.GetBalanceAsync(_apiKey, cancellationToken);
            Balances.Clear();
            foreach (var balance in snapshot.Balances)
            {
                Balances.Add(balance);
            }
            _hasBalanceResult = true;
            IsAvailable = snapshot.IsAvailable;
            StatusText = snapshot.IsAvailable ? "连接正常" : "连接正常，余额不可用";
            LastUpdatedText = $"更新于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            OnPropertyChanged(nameof(HasBalances));
            OnPropertyChanged(nameof(AvailabilityText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "查询已取消";
        }
        catch (DeepSeekAccountException exception)
        {
            SetError(exception.Error.Code, exception.Error.UserMessage);
        }
        catch (Exception)
        {
            SetError("API-E606", "无法查询 DeepSeek 账户信息");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ClearApiKey()
    {
        _apiKey = null;
        Balances.Clear();
        _hasBalanceResult = false;
        IsAvailable = false;
        ErrorText = null;
        StatusText = "尚未连接";
        LastUpdatedText = "尚未查询";
        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(HasBalances));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(MaskedApiKey));
        OnPropertyChanged(nameof(AvailabilityText));
    }

    private void SetError(string code, string message)
    {
        ErrorText = $"{code} · {message}";
        StatusText = "查询失败";
        OnPropertyChanged(nameof(HasError));
    }

    private void OpenTopUp() => _linkLauncher.Open(OfficialResource.DeepSeekTopUp);

    private static string MaskApiKey(string apiKey) => apiKey.Length <= 4
        ? new string('*', apiKey.Length)
        : $"****{apiKey[^4..]}";
}
