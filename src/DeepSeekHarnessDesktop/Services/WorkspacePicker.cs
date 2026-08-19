using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Windows.Forms;

namespace DeepSeekHarnessDesktop.Services;

public sealed class WorkspacePicker : IWorkspacePicker
{
    public string? Pick(string currentPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 DeepSeek Harness 工作目录",
            SelectedPath = Directory.Exists(currentPath) ? currentPath : string.Empty,
            ShowNewFolderButton = true,
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
