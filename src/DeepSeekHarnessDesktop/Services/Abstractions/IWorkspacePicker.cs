namespace DeepSeekHarnessDesktop.Services.Abstractions;

public interface IWorkspacePicker
{
    string? Pick(string currentPath);
}
