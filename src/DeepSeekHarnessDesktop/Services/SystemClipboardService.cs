using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Runtime.InteropServices;
using System.Windows;

namespace DeepSeekHarnessDesktop.Services;

public sealed class SystemClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            throw new HarnessException(new HarnessError(
                "APP-E510",
                "无法复制到剪贴板，请稍后重试",
                exception.Message,
                true,
                exception));
        }
    }
}
