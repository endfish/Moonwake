using System.Runtime.InteropServices;

namespace Moonwake.Infrastructure;

internal static class ConsoleWindow
{
    private const int SwHide = 0;

    public static void HideIfOwnedByCurrentProcess()
    {
        var processIds = new uint[2];
        if (GetConsoleProcessList(processIds, (uint)processIds.Length) != 1)
            return;

        var window = GetConsoleWindow();
        if (window != IntPtr.Zero)
            ShowWindow(window, SwHide);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(
        [Out] uint[] processList,
        uint processCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
