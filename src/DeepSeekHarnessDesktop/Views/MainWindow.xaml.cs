using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace DeepSeekHarnessDesktop.Views;

public partial class MainWindow : System.Windows.Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly DependencyDiagnosticsResult _diagnostics;
    private readonly AccountViewModel _accountViewModel;

    public MainWindow(
        MainWindowViewModel viewModel,
        WebViewNavigationService navigation,
        AppSettings settings,
        DependencyDiagnosticsResult diagnostics,
        AccountViewModel accountViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        _diagnostics = diagnostics;
        _accountViewModel = accountViewModel;
        DataContext = viewModel;
        navigation.Attach(Browser);
        viewModel.OpenLogsRequested += OnOpenLogsRequested;
        ApplyWindowSettings();
        Closing += CaptureWindowSettings;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow
        {
            Owner = this,
            DataContext = _diagnostics,
        }.ShowDialog();
    }

    private void OnAccountClick(object sender, RoutedEventArgs e)
    {
        new AccountWindow(_accountViewModel)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void ApplyWindowSettings()
    {
        Width = Math.Max(MinWidth, _settings.Window.Width);
        Height = Math.Max(MinHeight, _settings.Window.Height);
        if (_settings.Window.Left is { } left
            && _settings.Window.Top is { } top
            && left + Width > SystemParameters.VirtualScreenLeft
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top + Height > SystemParameters.VirtualScreenTop
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            Left = left;
            Top = top;
        }

        if (_settings.Window.IsMaximized)
        {
            Loaded += (_, _) => WindowState = WindowState.Maximized;
        }
    }

    private void CaptureWindowSettings(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        _settings.Window = new WindowSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = Math.Max(MinWidth, bounds.Width),
            Height = Math.Max(MinHeight, bounds.Height),
            IsMaximized = WindowState == WindowState.Maximized,
        };
    }

    private void OnOpenLogsRequested(object? sender, EventArgs e)
    {
        var window = new LogWindow
        {
            Owner = this,
            DataContext = _viewModel,
        };
        window.Show();
        if (_viewModel.RecentLogs.Count > 0)
        {
            window.LogList.ScrollIntoView(_viewModel.RecentLogs[^1]);
        }
    }

    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.F5 && _viewModel.ReloadPageCommand.CanExecute(null))
        {
            _viewModel.ReloadPageCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.R
                 && modifiers.HasFlag(ModifierKeys.Control)
                 && modifiers.HasFlag(ModifierKeys.Alt)
                 && _viewModel.RestartCommand.CanExecute(null))
        {
            if (System.Windows.MessageBox.Show(
                    this,
                    "确定要重启当前 DSH 实例吗？",
                    "重启 DSH",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            {
                _viewModel.RestartCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.L
                 && modifiers.HasFlag(ModifierKeys.Control)
                 && modifiers.HasFlag(ModifierKeys.Alt))
        {
            _viewModel.OpenLogsCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F6)
        {
            if (Browser.IsKeyboardFocusWithin)
            {
                WorkspaceTextBox.Focus();
            }
            else
            {
                Browser.Focus();
            }
            e.Handled = true;
        }
    }
}
