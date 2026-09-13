using System.Windows.Controls;
using System.Windows;
using DeepSeekHarnessDesktop.ViewModels;

namespace DeepSeekHarnessDesktop.Views.States;

public partial class FailedView : UserControl
{
    public FailedView() => InitializeComponent();

    private async void OnConnectExternalClick(object sender, RoutedEventArgs e)
    {
        var link = AuthenticationLink.Password;
        AuthenticationLink.Clear();
        if (DataContext is MainWindowViewModel viewModel)
            await viewModel.ConnectExternalAsync(link);
    }

    private void OnAuthenticationLinkUnloaded(object sender, RoutedEventArgs e) => AuthenticationLink.Clear();
}
