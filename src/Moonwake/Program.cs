using System.Text;

using Moonwake;
using Moonwake.Infrastructure;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            return StandaloneApplication.RunGui();
        }

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        return StandaloneApplication.RunAsync(args).GetAwaiter().GetResult();
    }
}
