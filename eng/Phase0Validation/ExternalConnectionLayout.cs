using DeepSeekHarnessDesktop.Views.States;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace DeepSeekHarnessDesktop.Phase0Validation;

internal static class ExternalConnectionLayout
{
    public static int Run()
    {
        // Render the real compiled control with the application's current resource dictionary.
        var document = new XmlDocument();
        document.Load("src/DeepSeekHarnessDesktop/App.xaml");
        var resources = document.DocumentElement!.FirstChild!.InnerXml;
        Application.Current.Resources = (ResourceDictionary)XamlReader.Parse(
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
            + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" + resources + "</ResourceDictionary>");
        foreach (var scale in new[] { 1.0, 1.25, 1.5 }) Render(scale);
        Console.WriteLine("PASS: External connection panel fits minimum content area at 100/125/150% render scale.");
        return 0;
    }

    private static void Render(double scale)
    {
        var view = new FailedView
        {
            Width = 760, Height = 400, Background = Brushes.White,
            DataContext = new
            {
                StatusTitle = "已有服务等待认证",
                StatusDetail = "DSH-E228 · 本机服务需要认证，请使用 DSH 终端中的认证链接连接",
                CanConnectExternal = true, ExternalConnectionAddress = "http://127.0.0.1:3080",
            },
        };
        view.Measure(new Size(view.Width, view.Height));
        view.Arrange(new Rect(0, 0, view.Width, view.Height));
        view.UpdateLayout();
        var panel = (StackPanel)((Grid)view.Content).Children[0];
        if (panel.DesiredSize.Height > view.Height || panel.DesiredSize.Width > view.Width)
            throw new InvalidOperationException("External connection panel exceeds minimum window content bounds.");
        var input = (PasswordBox)view.FindName("AuthenticationLink");
        if (!input.Focusable || !input.IsTabStop || input.ActualWidth < 300)
            throw new InvalidOperationException("Authentication input cannot be reached or is too narrow.");
        var image = new RenderTargetBitmap((int)(view.Width * scale), (int)(view.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory("output/validation");
        using var stream = File.Create($"output/validation/external-connection-{scale * 100:0}.png");
        encoder.Save(stream);
    }
}
