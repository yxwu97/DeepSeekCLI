using DeepSeekHarnessDesktop.Models;

namespace DeepSeekHarnessDesktop.Utilities;

public static class DshUpdateErrorMapper
{
    public static HarnessError ResourceMismatch(string technicalMessage, Exception? exception = null) =>
        Error("DSH-E223", "DSH 更新资源校验失败", technicalMessage, exception);

    public static HarnessError ActivationValidationFailed(string technicalMessage, Exception? exception = null) =>
        Error("DSH-E224", "DSH 安装内容校验失败", technicalMessage, exception);

    public static HarnessError SmokeFailed(string technicalMessage, Exception? exception = null) =>
        Error("DSH-E225", "DSH 安装后运行验证失败", technicalMessage, exception);

    public static HarnessError CatalogRejected(string technicalMessage, Exception? exception = null) =>
        Error("DSH-E226", "DSH 更新目录无效，已忽略远程版本", technicalMessage, exception);

    public static HarnessError ProtocolIncompatible(string technicalMessage, Exception? exception = null) =>
        Error("DSH-E227", "该 DSH 版本需要更高版本的 Desktop 或 Node.js", technicalMessage, exception);

    private static HarnessError Error(
        string code,
        string userMessage,
        string technicalMessage,
        Exception? exception) =>
        new(code, userMessage, technicalMessage, true, exception);
}
