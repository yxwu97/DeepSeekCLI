using System.Windows.Threading;

namespace DeepSeekHarnessDesktop.Services;

internal static class WebViewDispatcher
{
    public static async Task InvokeAsync(
        Dispatcher dispatcher,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dispatcher.CheckAccess())
        {
            await action();
            return;
        }

        await dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }
}
