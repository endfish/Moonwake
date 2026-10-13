using System.Text;

using Moonwake;
using Moonwake.Infrastructure;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            ConsoleWindow.HideIfOwnedByCurrentProcess();
            ApplicationConfiguration.Initialize();
            return StandaloneApplication.RunGui();
        }

        return StandaloneApplication.RunAsync(args).GetAwaiter().GetResult();
    }
}
