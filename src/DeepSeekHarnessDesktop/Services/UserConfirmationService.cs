using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Windows;

namespace DeepSeekHarnessDesktop.Services;

public sealed class UserConfirmationService : IUserConfirmationService
{
    public bool ConfirmServiceRestart(Uri currentUri, Uri newUri) => MessageBox.Show(
        Application.Current?.MainWindow,
        $"应用新的服务地址需要重启当前 DSH。\n\n当前：{currentUri}\n新的：{newUri}\n\n是否继续？",
        "应用服务地址",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmDshDownload() => MessageBox.Show(
        Application.Current?.MainWindow,
        "应用将通过 npx 准备并启动 npm 当前发布的 DSH。此操作可能访问 npm registry 并写入当前用户缓存，最长等待 5 分钟。是否继续？",
        "准备并启动 DSH",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmClearChatData() => MessageBox.Show(
        Application.Current?.MainWindow,
        "这将清除 DeepSeek Chat 专用浏览器配置中的登录会话、站点数据和已保存密码，不影响 Code 页面或 DSH。是否继续？",
        "清除 Chat 登录信息",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
