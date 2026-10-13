using System.ComponentModel;
using System.Diagnostics;

using Moonwake.Models;

namespace Moonwake.Services;

internal sealed class GameProcessService
{
    public IReadOnlyList<GameProcessInfo> Discover()
    {
        var results = new List<GameProcessInfo>();
        foreach (var process in Process.GetProcessesByName("ffxiv_dx11").OrderBy(static process => process.Id))
        {
            using (process)
                results.Add(Inspect(process));
        }

        return results;
    }

    public GameProcessInfo? Find(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!string.Equals(process.ProcessName, "ffxiv_dx11", StringComparison.OrdinalIgnoreCase))
                return null;

            return Inspect(process);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static GameProcessInfo Inspect(Process process)
    {
        string? executablePath = null;
        string? inspectionError = null;

        try
        {
            executablePath = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            inspectionError = $"无法读取主模块：{ex.Message}";
        }

        var moduleState = DalamudModuleState.Unknown;
        long? startTimeUtcTicks = null;
        try
        {
            moduleState = DalamudModuleState.NotLoaded;
            startTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            foreach (ProcessModule module in process.Modules)
            {
                if (!string.Equals(module.ModuleName, "Dalamud.dll", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(module.ModuleName, "Dalamud.Boot.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                moduleState = DalamudModuleState.Loaded;
                break;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            moduleState = DalamudModuleState.Unknown;
            inspectionError = string.IsNullOrWhiteSpace(inspectionError)
                                  ? $"无法枚举模块：{ex.Message}"
                                  : $"{inspectionError}；无法枚举模块：{ex.Message}";
        }

        return new GameProcessInfo(
            process.Id,
            executablePath,
            GameVersionReader.TryRead(executablePath),
            moduleState,
            inspectionError,
            startTimeUtcTicks);
    }
}
