using DeepSeekHarnessDesktop.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace DeepSeekHarnessDesktop.Views;

public partial class AccountWindow : System.Windows.Window
{
    private readonly AccountViewModel _viewModel;
    private readonly CancellationTokenSource _closingCancellation = new();

    public AccountWindow(AccountViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closed += (_, _) => _closingCancellation.Cancel();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        var candidateApiKey = ApiKeyBox.Password;
        ApiKeyBox.Clear();
        await _viewModel.RefreshAsync(candidateApiKey, _closingCancellation.Token);
    }

    private void OnClearApiKeyClick(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Clear();
        _viewModel.ClearApiKey();
        ApiKeyBox.Focus();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
