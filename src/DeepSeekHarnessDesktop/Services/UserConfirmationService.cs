using DeepSeekHarnessDesktop.Services.Abstractions;
using DeepSeekHarnessDesktop.Utilities;
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
        $"应用将从 npm registry 下载锁定的 DSH {DshPackageMetadata.ValidatedVersion} 完整依赖图。\n\n"
        + "安装位置：%LOCALAPPDATA%\\DeepSeekHarnessDesktop\\dsh\n"
        + "预计磁盘占用：约 300 MiB\n\n"
        + "安装成功后会直接复用，不会在每次启动时重复下载。是否继续？",
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
