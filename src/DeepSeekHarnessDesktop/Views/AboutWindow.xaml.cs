using System.Windows;
using DeepSeekHarnessDesktop.ViewModels;

namespace DeepSeekHarnessDesktop.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as AboutViewModel)?.Cancel();
    }
}
