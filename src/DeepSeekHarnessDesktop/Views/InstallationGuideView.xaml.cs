using DeepSeekHarnessDesktop.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DeepSeekHarnessDesktop.Views;

public partial class InstallationGuideView : UserControl
{
    private InstallationGuideViewModel? _viewModel;
    private bool _followLatest = true;

    public InstallationGuideView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach(DataContext as InstallationGuideViewModel);

    private void OnUnloaded(object sender, RoutedEventArgs e) => Attach(null);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Attach(e.NewValue as InstallationGuideViewModel);
        }
    }

    private void Attach(InstallationGuideViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.RecentLogs.CollectionChanged -= OnLogsChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.RecentLogs.CollectionChanged += OnLogsChanged;
        }
    }

    private void OnLogScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
        {
            _followLatest = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 1;
        }
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_followLatest || _viewModel?.RecentLogs.Count is not > 0)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_followLatest && _viewModel?.RecentLogs.Count is > 0)
            {
                InstallLogList.ScrollIntoView(_viewModel.RecentLogs[^1]);
            }
        }, DispatcherPriority.Background);
    }
}
