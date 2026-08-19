using System.Diagnostics;
using System.Reflection;

namespace DeepSeekHarnessDesktop.TestHarness;

public sealed class HarnessMarker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;
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
                    await Task.Run(() => child.WaitForExit());
                }
                return 0;
            case "--spawn-and-exit":
                var survivor = StartSelf("--child");
                Console.WriteLine($"CHILD_PID={survivor.Id}");
                Console.Out.Flush();
                survivor.Dispose();
                return 0;
            case "--child":
                await Task.Delay(TimeSpan.FromSeconds(30));
                return 0;
            case "--echo-args":
                Console.WriteLine("ARGS=" + string.Join("|", args.Skip(1)));
                return 0;
            default:
                return 2;
        }
    }

    private static Process StartSelf(string mode)
    {
        var host = Assembly.GetExecutingAssembly().Location;
        var startInfo = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Arguments = mode;
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start fixture child.");
    }
}
