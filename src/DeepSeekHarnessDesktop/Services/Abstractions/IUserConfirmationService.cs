namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IUserConfirmationService
{
    bool ConfirmServiceRestart(Uri currentUri, Uri newUri);
    bool ConfirmDshDownload();
    bool ConfirmClearChatData();
}
