using DeepSeekHarnessDesktop.ViewModels;
using System.Windows;

namespace DeepSeekHarnessDesktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += (_, _) => viewModel.Cancel();
    }
}
