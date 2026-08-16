using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace DeepSeekHarnessDesktop.Phase0Validation;

public partial class MainWindow : Window
{
    private readonly TaskCompletionSource<(bool Succeeded, string Message)> _initialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _acceleratorRouted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Path.GetTempPath(),
                "DeepSeekHarnessDesktop",
                "Phase0WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);

            Browser.PreviewKeyDown += OnBrowserPreviewKeyDown;
            Browser.NavigateToString(
                "<html><head><title>Phase 0</title></head>" +
                "<body><h1>WebView2 initialized</h1><p>Press F5 to test host routing.</p></body></html>");
            StatusText.Text = $"PASS: WebView2 {Browser.CoreWebView2.Environment.BrowserVersionString}; accelerator route attached.";
            _initialization.TrySetResult((true, StatusText.Text));
        }
        catch (Exception exception)
        {
            StatusText.Text = $"FAIL: {exception.Message}";
            _initialization.TrySetResult((false, StatusText.Text));
        }
    }

    public Task<(bool Succeeded, string Message)> WaitForInitializationAsync() =>
        _initialization.Task;

    public async Task<bool> VerifyAcceleratorRouteAsync()
    {
        Activate();
        Browser.Focus();
        await Task.Delay(500);

        NativeMethods.KeybdEvent(0x74, 0, 0, 0);
        NativeMethods.KeybdEvent(0x74, 0, 0x0002, 0);
        try
        {
            await _acceleratorRouted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private void OnBrowserPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 || e.SystemKey == Key.F5)
        {
            e.Handled = true;
            StatusText.Text = "PASS: F5 reached the host accelerator route.";
            _acceleratorRouted.TrySetResult();
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "keybd_event")]
        internal static extern void KeybdEvent(
            byte virtualKey,
            byte scanCode,
            uint flags,
            nuint extraInfo);
    }
}
