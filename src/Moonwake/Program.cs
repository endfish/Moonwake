using System.Text;

using Moonwake;
using Moonwake.Infrastructure;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update")
        {
            ApplicationConfiguration.Initialize();
            return Moonwake.Services.UpdateInstaller.RunWorker(args[1]);
        }
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
