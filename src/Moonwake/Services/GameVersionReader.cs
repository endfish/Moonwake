namespace Moonwake.Services;

internal static class GameVersionReader
{
    public static string? TryRead(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return null;

        var candidates = new[]
        {
            Path.Combine(executableDirectory, "ffxivgame.ver"),
            Path.Combine(executableDirectory, "..", "ffxivgame.ver"),
            Path.Combine(executableDirectory, "..", "game", "ffxivgame.ver"),
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;

                var version = File.ReadAllText(candidate).Trim().TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }
            catch (IOException)
            {
                // Try the next conventional location.
            }
            catch (UnauthorizedAccessException)
            {
                // Try the next conventional location.
            }
        }

        return null;
    }
}
