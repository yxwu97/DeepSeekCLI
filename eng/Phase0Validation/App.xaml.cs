using System.Windows;

namespace DeepSeekHarnessDesktop.Phase0Validation;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await Phase0Runner.RunSelfTestsAsync();
            Shutdown(exitCode);
            return;
        }

        if (e.Args.Contains("--chat-webview-smoke", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var chatSmokeWindow = new MainWindow();
            MainWindow = chatSmokeWindow;
            chatSmokeWindow.Show();

            try
            {
                var result = await chatSmokeWindow.WaitForInitializationAsync()
                    .WaitAsync(TimeSpan.FromSeconds(30));
                await Console.Out.WriteLineAsync(result.Message);
                chatSmokeWindow.Close();
                await chatSmokeWindow.CleanupAsync();
                Shutdown(result.Succeeded ? 0 : 1);
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync($"FAIL: Chat WebView2 smoke: {exception.Message}");
                chatSmokeWindow.Close();
                await chatSmokeWindow.CleanupAsync();
                Shutdown(1);
            }

            return;
        }

        if (e.Args.Contains("--webview-smoke", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var smokeWindow = new MainWindow();
            MainWindow = smokeWindow;
            smokeWindow.Show();

            try
            {
                var result = await smokeWindow.WaitForInitializationAsync()
                    .WaitAsync(TimeSpan.FromSeconds(30));
                await Console.Out.WriteLineAsync(result.Message);
                var acceleratorRouted = result.Succeeded
                    && await smokeWindow.VerifyAcceleratorRouteAsync();
                await Console.Out.WriteLineAsync(acceleratorRouted
                    ? "PASS: F5 from the focused WebView2 HWND reached WPF PreviewKeyDown."
                    : "FAIL: F5 did not reach the host shortcut route.");
                smokeWindow.Close();
                await smokeWindow.CleanupAsync();
                Shutdown(result.Succeeded && acceleratorRouted ? 0 : 1);
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync($"FAIL: WebView2 smoke: {exception.Message}");
                smokeWindow.Close();
                await smokeWindow.CleanupAsync();
                Shutdown(1);
            }

            return;
        }

        if (e.Args.Contains("--job-parent", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await Phase0Runner.RunJobParentAsync();
            Shutdown(exitCode);
            return;
        }

        if (e.Args.Contains("--job-child", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await Task.Delay(TimeSpan.FromSeconds(30));
            Shutdown(0);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
