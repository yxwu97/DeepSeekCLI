using DeepSeekHarnessDesktop.Models;
using DeepSeekHarnessDesktop.Services.Abstractions;
using System.Diagnostics;

namespace DeepSeekHarnessDesktop.Services;

public sealed class PowerShellTerminalLauncher : ITerminalLauncher
{
    public void OpenPowerShell(string workingDirectory)
    {
        if (!PathCompatibility.IsFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw Error("工作目录不存在，无法打开 PowerShell", $"Invalid terminal working directory: {workingDirectory}");
        }

        var executable = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetFullPath(workingDirectory),
            };
            startInfo.AddArgument("-NoExit");
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw Error("无法打开 PowerShell，请手动打开终端", exception.Message, exception);
        }
    }

    private static HarnessException Error(string userMessage, string technicalMessage, Exception? exception = null) =>
        new(new HarnessError("APP-E511", userMessage, technicalMessage, true, exception));
}
