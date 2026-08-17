using DeepSeekHarnessDesktop.Services;
using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.ViewModels;
using DeepSeekHarnessDesktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace DeepSeekHarnessDesktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private LogService? _logService;
    private SingleInstanceService? _singleInstance;
    private ISettingsService? _settingsService;
    private Models.AppSettings? _settings;
    private ILogger? _logger;
    private TrayIconService? _trayIcon;
    private int _shutdownStarted;
    private bool _shutdownCompleted;
    private bool _exitRequested;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logService = new LogService();
        _logger = _logService.CreateLogger(nameof(App));
        _logger.LogInformation(new EventId(1000), "DeepSeek Harness Desktop is starting.");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            var notified = await _singleInstance.NotifyPrimaryAsync(CancellationToken.None);
            _logger.LogInformation(new EventId(1002), "Secondary instance exiting; activation delivered: {Delivered}.", notified);
            Shutdown();
            return;
        }

        _settingsService = new SettingsService(_logService.CreateLogger<SettingsService>());
        _settings = await _settingsService.LoadAsync(CancellationToken.None);
        var diagnosticsService = new DependencyDiagnosticsService();
        var diagnostics = await diagnosticsService.DiagnoseAsync(CancellationToken.None);
        foreach (var error in diagnostics.Errors)
        {
            _logger.LogWarning(new EventId(1010), "{Code}: {Message}", error.Code, error.TechnicalMessage);
        }
        _services = new ServiceCollection()
            .AddSingleton(_settings)
            .AddSingleton(diagnostics)
            .AddSingleton<IDependencyDiagnosticsService>(diagnosticsService)
            .AddSingleton(_settingsService)
            .AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            .AddSingleton<IDeepSeekApiKeyProvider, DeepSeekApiKeyProvider>()
            .AddSingleton<IDeepSeekAccountService, DeepSeekAccountService>()
            .AddSingleton<AccountViewModel>()
            .AddSingleton<IDshReleaseService>(_ => new DshReleaseService())
            .AddSingleton<IExternalLinkLauncher, ExternalLinkLauncher>()
            .AddSingleton<IUserConfirmationService, UserConfirmationService>()
            .AddSingleton<TrayIconService>()
            .AddSingleton<HarnessStateMachine>()
            .AddSingleton<IRecentLogBuffer, RecentLogBuffer>()
            .AddSingleton<IWorkspacePicker, WorkspacePicker>()
            .AddSingleton<IDshCommandResolver, DshCommandResolver>()
            .AddSingleton<IHarnessProcessManager, HarnessProcessManager>()
            .AddSingleton<IHarnessHealthMonitor, HarnessHealthMonitor>()
            .AddSingleton<IRuntimeHealthWatcher, RuntimeHealthWatcher>()
            .AddSingleton<IWebViewEnvironmentProvider, WebViewEnvironmentProvider>()
            .AddSingleton<CodeWebViewService>()
            .AddSingleton<ICodeWebViewService>(services => services.GetRequiredService<CodeWebViewService>())
            .AddSingleton<ChatWebViewService>()
            .AddSingleton<IChatWebViewService>(services => services.GetRequiredService<ChatWebViewService>())
            .AddSingleton<IHarnessLifecycleCoordinator, HarnessLifecycleCoordinator>()
            .AddSingleton<InstallationGuideViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<AboutViewModel>()
            .AddSingleton<MainWindowViewModel>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider(validateScopes: true);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Closing += OnMainWindowClosing;
        _trayIcon = _services.GetRequiredService<TrayIconService>();
        _trayIcon.Initialize(ActivateMainWindow, RequestApplicationExit);
        _singleInstance.StartListening(() => Dispatcher.InvokeAsync(
            ActivateMainWindow,
            DispatcherPriority.Send).Task);
        window.Show();
        try
        {
            await _services.GetRequiredService<ICodeWebViewService>()
                .InitializeAsync(CancellationToken.None);
            await _services.GetRequiredService<IHarnessLifecycleCoordinator>()
                .InitializeAsync(CancellationToken.None);
        }
        catch (Models.HarnessException exception)
        {
            System.Windows.MessageBox.Show(
                window,
                $"{exception.Error.Code}\n{exception.Error.UserMessage}",
                "DeepSeek Harness Desktop",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        var window = (System.Windows.Window)sender!;
        if (!_exitRequested)
        {
            window.Hide();
            _trayIcon?.ShowHiddenNotification();
            _logger?.LogInformation(new EventId(1005), "Main window hidden to the system tray.");
            return;
        }

        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 1)
        {
            return;
        }

        window.IsEnabled = false;
        _logger?.LogInformation(new EventId(1003), "Application shutdown requested.");
        var cleanup = CleanupPrimaryInstanceAsync();
        if (await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(8))) == cleanup)
        {
            await cleanup;
        }
        else
        {
            _logger?.LogWarning(new EventId(1004), "Shutdown cleanup exceeded 8 seconds; process Job handles will close with the host.");
        }

        await DisposeSingleInstanceAsync();
        FlushExitLog();
        _shutdownCompleted = true;
        window.Close();
        Shutdown();
    }

    private async Task CleanupPrimaryInstanceAsync()
    {
        if (_settingsService is not null && _settings is not null)
        {
            try
            {
                await _settingsService.SaveAsync(_settings, CancellationToken.None);
            }
            catch (Models.HarnessException exception)
            {
                _logger?.LogError(new EventId(1111), exception, "{Code}: {Message}", exception.Error.Code, exception.Error.TechnicalMessage);
            }
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
            _services = null;
            _trayIcon = null;
        }
    }

    private async Task DisposeSingleInstanceAsync()
    {
        if (_singleInstance is not null)
        {
            await _singleInstance.DisposeAsync();
            _singleInstance = null;
        }
    }

    private void FlushExitLog()
    {
        _logger?.LogInformation(new EventId(1001), "DeepSeek Harness Desktop exited.");
        _logger = null;
        _logService?.Dispose();
        _logService = null;
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is not { } window)
        {
            return;
        }
        if (window.WindowState == System.Windows.WindowState.Minimized)
        {
            window.WindowState = System.Windows.WindowState.Normal;
        }
        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void RequestApplicationExit()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RequestApplicationExit);
            return;
        }

        if (_shutdownCompleted || _exitRequested)
        {
            return;
        }

        _exitRequested = true;
        if (MainWindow is { } window)
        {
            window.Close();
        }
        else
        {
            Shutdown();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _exitRequested = true;
        _logger?.LogInformation(new EventId(1006), "Windows session ending; application will exit.");
        base.OnSessionEnding(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(new EventId(9000), e.Exception, "APP-E599: Unhandled dispatcher exception.");
        e.Handled = false;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(new EventId(9001), e.ExceptionObject as Exception, "APP-E599: Unhandled application exception.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _services = null;
        }
        if (_singleInstance is not null)
        {
            _singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _singleInstance = null;
        }
        FlushExitLog();
        base.OnExit(e);
    }
}
