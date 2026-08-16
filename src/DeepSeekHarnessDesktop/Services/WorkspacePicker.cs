using DeepSeekHarnessDesktop.Services.Abstractions;
using Microsoft.Win32;

namespace DeepSeekHarnessDesktop.Services;

public sealed class WorkspacePicker : IWorkspacePicker
{
    public string? Pick(string currentPath)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 DeepSeek Harness 工作目录",
            InitialDirectory = Directory.Exists(currentPath) ? currentPath : null,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
