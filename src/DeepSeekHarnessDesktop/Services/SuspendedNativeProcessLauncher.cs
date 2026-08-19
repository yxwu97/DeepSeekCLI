using DeepSeekHarnessDesktop.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace DeepSeekHarnessDesktop.Services;

internal static class SuspendedNativeProcessLauncher
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartfUseStdHandles = 0x00000100;

    public static SuspendedProcessLaunch Start(DshLaunchOptions options, WindowsJobObject job)
    {
        var command = BuildLaunchCommand(options);
        var stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var environment = BuildEnvironmentBlock(options.Environment);
        var commandLine = new StringBuilder(command.CommandLine);
        var startupInfo = CreateStartupInfo(stdout, stderr, stdin);
        ProcessInformation processInfo = default;
        try
        {
            if (!NativeMethods.CreateProcess(
                    command.ExecutablePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment,
                    environment,
                    options.WorkingDirectory,
                    ref startupInfo,
                    out processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessW failed.");
            }
            job.Assign(processInfo.ProcessHandle);
            var process = Process.GetProcessById((int)processInfo.ProcessId);
            process.EnableRaisingEvents = true;
            stdout.DisposeLocalCopyOfClientHandle();
            stderr.DisposeLocalCopyOfClientHandle();
            stdin.DisposeLocalCopyOfClientHandle();
            stdin.Dispose();
            var launch = new SuspendedProcessLaunch(process, stdout, stderr, processInfo.ThreadHandle);
            processInfo.ThreadHandle = IntPtr.Zero;
            return launch;
        }
        catch
        {
            if (processInfo.ProcessHandle != IntPtr.Zero)
            {
                NativeMethods.TerminateProcess(processInfo.ProcessHandle, 1);
            }
            stdout.Dispose();
            stderr.Dispose();
            stdin.Dispose();
            throw;
        }
        finally
        {
            if (processInfo.ThreadHandle != IntPtr.Zero) NativeMethods.CloseHandle(processInfo.ThreadHandle);
            if (processInfo.ProcessHandle != IntPtr.Zero) NativeMethods.CloseHandle(processInfo.ProcessHandle);
            Marshal.FreeHGlobal(environment);
        }
    }

    private static LaunchCommand BuildLaunchCommand(DshLaunchOptions options)
    {
        if (!Directory.Exists(options.WorkingDirectory))
        {
            throw new DirectoryNotFoundException(options.WorkingDirectory);
        }

        var extension = Path.GetExtension(options.ExecutablePath);
        if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var startInfo = string.Equals(
                Path.GetFileName(options.ExecutablePath),
                "npm.cmd",
                StringComparison.OrdinalIgnoreCase)
                    ? NpmCommandLineBuilder.Build(
                        options.ExecutablePath,
                        options.Arguments,
                        options.WorkingDirectory,
                        options.Environment)
                    : CmdCommandLineBuilder.Build(
                        options.ExecutablePath,
                        options.Arguments,
                        options.WorkingDirectory,
                        options.Environment);
            return new LaunchCommand(
                startInfo.FileName,
                $"{QuoteArgument(startInfo.FileName)} {startInfo.Arguments}");
        }

        if (!File.Exists(options.ExecutablePath)
            || (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Owned processes require an existing .exe or .com executable.");
        }

        return new LaunchCommand(
            options.ExecutablePath,
            BuildCommandLine(options.ExecutablePath, options.Arguments));
    }

    private static StartupInfo CreateStartupInfo(
        AnonymousPipeServerStream stdout,
        AnonymousPipeServerStream stderr,
        AnonymousPipeServerStream stdin) => new()
        {
            Size = Marshal.SizeOf<StartupInfo>(),
            Flags = StartfUseStdHandles,
            StandardOutput = stdout.ClientSafePipeHandle.DangerousGetHandle(),
            StandardError = stderr.ClientSafePipeHandle.DangerousGetHandle(),
            StandardInput = stdin.ClientSafePipeHandle.DangerousGetHandle(),
        };

    private static nint BuildEnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            values[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }
        foreach (var pair in overrides)
        {
            var name = pair.Key;
            var value = pair.Value;
            if (name.Contains('=') || name.IndexOf('\0') >= 0 || value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Process environment contains an invalid name or value.");
            }
            values[name] = value;
        }
        var block = string.Join("\0", values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";
        return Marshal.StringToHGlobalUni(block);
    }

    private static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var values = new[] { executable }.Concat(arguments);
        return string.Join(" ", values.Select(QuoteArgument));
    }

    internal static string QuoteArgument(string value)
    {
        if (value.IndexOf('\0') >= 0 || value.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("Process argument contains an invalid character.");
        }
        if (value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return value;
        }
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append(character);
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private sealed record LaunchCommand(string ExecutablePath, string CommandLine);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2;
        public nint ReservedPointer;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        public nint ProcessHandle;
        public nint ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(
            string applicationName,
            StringBuilder commandLine,
            nint processAttributes,
            nint threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            nint environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint ResumeThread(nint threadHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(nint processHandle, uint exitCode);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(nint handle);
    }
}

internal sealed class SuspendedProcessLaunch(
    Process process,
    AnonymousPipeServerStream standardOutput,
    AnonymousPipeServerStream standardError,
    nint threadHandle) : IDisposable
{
    private nint _threadHandle = threadHandle;

    public Process Process { get; } = process;
    public AnonymousPipeServerStream StandardOutput { get; } = standardOutput;
    public AnonymousPipeServerStream StandardError { get; } = standardError;

    public void Resume()
    {
        var handle = Interlocked.Exchange(ref _threadHandle, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("The process has already been resumed.");
        }
        try
        {
            if (SuspendedNativeProcessLauncher.NativeMethods.ResumeThread(handle) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed.");
            }
        }
        finally
        {
            SuspendedNativeProcessLauncher.NativeMethods.CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _threadHandle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            SuspendedNativeProcessLauncher.NativeMethods.TerminateProcess(Process.Handle, 1);
            SuspendedNativeProcessLauncher.NativeMethods.CloseHandle(handle);
        }
        StandardOutput.Dispose();
        StandardError.Dispose();
    }
}
