namespace Moonwake.Models;

internal enum DalamudModuleState
{
    NotLoaded,
    Loaded,
    Unknown,
}

internal sealed record GameProcessInfo(
    int ProcessId,
    string? ExecutablePath,
    string? GameVersion,
    DalamudModuleState DalamudState,
    string? InspectionError,
    long? StartTimeUtcTicks = null);
