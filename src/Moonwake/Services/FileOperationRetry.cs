namespace Moonwake.Services;

internal static class FileOperationRetry
{
    internal static void MoveDirectory(string source, string destination, Action<string, string>? move = null, Action<int>? wait = null)
    {
        move ??= Directory.Move;
        wait ??= Thread.Sleep;
        for (var attempt = 0; ; attempt++)
        {
            try { move(source, destination); return; }
            catch (Exception ex) when (attempt < 7 && ex is IOException or UnauthorizedAccessException &&
                (ex.HResult & 0xFFFF) is 5 or 32 or 33)
            {
                wait(Math.Min(100 << attempt, 2000));
            }
        }
    }
}
