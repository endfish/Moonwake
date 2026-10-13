using System.Diagnostics;
using Moonwake.Models;

namespace Moonwake.Services;

/// <summary>Owns a handle to one process instance, so PID reuse cannot change the tracked game.</summary>
internal sealed class GameSession : IDisposable
{
    private readonly Process process;

    private GameSession(Process process) => this.process = process;

    public bool HasExited => this.process.HasExited;

    public static GameSession Capture(GameProcessInfo target)
    {
        var process = Process.GetProcessById(target.ProcessId);
        try
        {
            _ = process.SafeHandle; // Keep the process object alive through injection and monitoring.
            if (target.StartTimeUtcTicks is null ||
                process.StartTime.ToUniversalTime().Ticks != target.StartTimeUtcTicks || process.HasExited)
                throw new InvalidOperationException("游戏进程已变化，请刷新后重新选择。");
            return new GameSession(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => this.process.WaitForExitAsync(cancellationToken);

    public void Dispose() => this.process.Dispose();
}
