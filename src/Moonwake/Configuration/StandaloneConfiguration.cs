namespace Moonwake.Configuration;

internal sealed class StandaloneConfiguration
{
    public int SchemaVersion { get; set; } = 1;

    public string PayloadDirectory { get; set; } = Path.Combine("payloads", "current");

    public string AssetDirectory { get; set; } = Path.Combine("assets", "current");

    public string RuntimeDirectory { get; set; } = "runtime";

    public List<string> SupportedGameVersions { get; set; } = ["2026.10.06.0000.0000"];

    public int PollIntervalSeconds { get; set; } = 2;

    public int InitializationDelayMs { get; set; }

    public bool LoadPluginsByDefault { get; set; } = true;

    public bool IsGameVersionSupported(string? version) => version is not null && this.SupportedGameVersions.Contains(version, StringComparer.Ordinal);

    public void Normalize()
    {
        this.SchemaVersion = 1;
        this.PayloadDirectory = NormalizePath(this.PayloadDirectory, Path.Combine("payloads", "current"));
        this.AssetDirectory = NormalizePath(this.AssetDirectory, Path.Combine("assets", "current"));
        this.RuntimeDirectory = NormalizePath(this.RuntimeDirectory, "runtime");
        this.SupportedGameVersions = (this.SupportedGameVersions ?? [])
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Select(static version => version.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        this.PollIntervalSeconds = Math.Clamp(this.PollIntervalSeconds, 1, 60);
        this.InitializationDelayMs = Math.Clamp(this.InitializationDelayMs, 0, 120_000);
    }

    private static string NormalizePath(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
