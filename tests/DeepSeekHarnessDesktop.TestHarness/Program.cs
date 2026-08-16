using System.Diagnostics;
using System.Reflection;

namespace DeepSeekHarnessDesktop.TestHarness;

public sealed class HarnessMarker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var mode = args.FirstOrDefault() ?? "--emit";
        switch (mode)
        {
            case "--emit":
                Console.WriteLine("\u001b[32mserver http://127.0.0.1:43123/\u001b[0m");
                Console.Error.WriteLine("fixture stderr");
                await Task.Delay(TimeSpan.FromSeconds(30));
                return 0;
            case "--exit":
                Console.Error.WriteLine("fixture immediate exit");
                return 23;
            case "--crash":
                Console.WriteLine("server http://127.0.0.1:43123/");
                Console.Out.Flush();
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                Console.Error.WriteLine("fixture runtime crash");
                return 24;
            case "--tree":
                using (var child = StartSelf("--child"))
                {
                    Console.WriteLine($"CHILD_PID={child.Id}");
                    Console.Out.Flush();
                    await child.WaitForExitAsync();
                }
                return 0;
            case "--child":
                await Task.Delay(TimeSpan.FromSeconds(30));
                return 0;
            default:
                return 2;
        }
    }

    private static Process StartSelf(string mode)
    {
        var host = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process host.");
        var startInfo = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }
        startInfo.ArgumentList.Add(mode);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start fixture child.");
    }
}
