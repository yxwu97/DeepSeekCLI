using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DeepSeekHarnessDesktop.Phase0Validation;

public partial class MainWindow : Window
{
    private string? _userDataFolder;
    private readonly TaskCompletionSource<(bool Succeeded, string Message)> _initialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _acceleratorRouted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _userDataFolder = Path.Combine(
                Path.GetTempPath(),
                "DeepSeekHarnessDesktop",
                "Phase0WebView2",
                Guid.NewGuid().ToString("N"));
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _userDataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            var chatOptions = environment.CreateCoreWebView2ControllerOptions();
            chatOptions.ProfileName = "Chat";
            chatOptions.IsInPrivateModeEnabled = false;
            await ChatBrowser.EnsureCoreWebView2Async(environment, chatOptions);

            await ValidateDualProfilesAsync();

            Browser.PreviewKeyDown += OnBrowserPreviewKeyDown;
            Browser.NavigateToString(
                "<html><head><title>Phase 0</title></head>" +
                "<body><h1>WebView2 initialized</h1><p>Press F5 to test host routing.</p></body></html>");
            StatusText.Text = $"PASS: WebView2 {Browser.CoreWebView2.Environment.BrowserVersionString}; dual profiles, state retention, profile clear and accelerator route ready.";
            _initialization.TrySetResult((true, StatusText.Text));
        }
        catch (Exception exception)
        {
            StatusText.Text = $"FAIL: {exception.Message}";
            _initialization.TrySetResult((false, StatusText.Text));
        }
    }

    private async Task ValidateDualProfilesAsync()
    {
        var codeProfile = Browser.CoreWebView2.Profile;
        var chatProfile = ChatBrowser.CoreWebView2.Profile;
        if (chatProfile.ProfileName != "Chat"
            || codeProfile.ProfilePath.Equals(chatProfile.ProfilePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Code and Chat WebView2 profiles are not isolated.");
        }

        chatProfile.IsPasswordAutosaveEnabled = true;
        chatProfile.IsGeneralAutofillEnabled = true;
        await NavigateAsync(ChatBrowser, "<html><body><input id='draft'><div id='scroll' style='height:80px;overflow:auto'><div style='height:1000px'></div></div></body></html>");
        await ChatBrowser.ExecuteScriptAsync(
            "document.getElementById('draft').value='draft-preserved';document.getElementById('scroll').scrollTop=120;");

        ChatBrowser.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Browser.Visibility = Visibility.Collapsed;
        ChatBrowser.Visibility = Visibility.Visible;
        var draft = await ChatBrowser.ExecuteScriptAsync("document.getElementById('draft').value");
        if (!draft.Equals("\"draft-preserved\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Collapsed Chat WebView2 did not preserve page state.");
        }

        await chatProfile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        ChatBrowser.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
    }

    private static async Task NavigateAsync(Microsoft.Web.WebView2.Wpf.WebView2 browser, string html)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                completion.TrySetResult();
            }
            else
            {
                completion.TrySetException(new InvalidOperationException($"Local navigation failed: {e.WebErrorStatus}."));
            }
        }

        browser.NavigationCompleted += OnCompleted;
        try
        {
            browser.NavigateToString(html);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            browser.NavigationCompleted -= OnCompleted;
        }
    }

    public Task<(bool Succeeded, string Message)> WaitForInitializationAsync() =>
        _initialization.Task;

    public async Task<bool> VerifyAcceleratorRouteAsync()
    {
        Activate();
        Topmost = true;
        Topmost = false;
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Browser.Focus();
        Keyboard.Focus(Browser);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(750);

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

    private void OnClosed(object? sender, EventArgs e)
    {
        Browser.Dispose();
        ChatBrowser.Dispose();
    }

    public async Task CleanupAsync()
    {
        if (_userDataFolder is not { } path)
        {
            return;
        }

        var expectedRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "DeepSeekHarnessDesktop", "Phase0WebView2")) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path);
        if (!candidate.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(candidate, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4)
                {
                    Console.Error.WriteLine($"Phase 0 temporary profile cleanup deferred: {exception.Message}");
                    return;
                }
                await Task.Delay(250);
            }
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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(nint windowHandle);
    }
}
