using System.Globalization;

namespace Moonwake.Infrastructure;

internal sealed class StandaloneLogger : IDisposable
{
    private readonly object syncRoot = new();
    private readonly StreamWriter writer;

    public StandaloneLogger(StandalonePaths paths)
    {
        Directory.CreateDirectory(paths.LogDirectory);
        var logPath = Path.Combine(
            paths.LogDirectory,
            $"moonwake-{DateTimeOffset.Now:yyyyMMdd}.log");
        this.writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
    }

    public void Info(string message) => this.Write("INFO", message, Console.Out);

    public void Warn(string message) => this.Write("WARN", message, Console.Error);

    public void Error(string message) => this.Write("ERROR", message, Console.Error);

    public void Dispose() => this.writer.Dispose();

    private void Write(string level, string message, TextWriter console)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}");

        lock (this.syncRoot)
        {
            console.WriteLine(line);
            this.writer.WriteLine(line);
        }
    }
}
