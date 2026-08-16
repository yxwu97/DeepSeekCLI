using System.Windows;
using System.Windows.Input;

namespace DeepSeekHarnessDesktop.Views;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Close();
            }
        };
    }
}
