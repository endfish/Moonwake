using System.Text.Json;
using Moonwake.Configuration;

namespace Moonwake.Infrastructure;

internal sealed class StandalonePaths
{
    private StandalonePaths(string homeDirectory)
    {
        this.HomeDirectory = homeDirectory;
        this.ConfigurationDirectory = Path.Combine(homeDirectory, "config");
        this.InstalledPluginsDirectory = Path.Combine(homeDirectory, "installedPlugins");
        this.PluginConfigurationsDirectory = Path.Combine(homeDirectory, "pluginConfigs");
        this.LogDirectory = Path.Combine(homeDirectory, "logs");
        this.ImportDirectory = Path.Combine(homeDirectory, "imports");
        this.TempDirectory = Path.Combine(homeDirectory, "temp");
        this.StandaloneConfigurationPath = Path.Combine(this.ConfigurationDirectory, "moonwake.json");
        this.DalamudConfigurationPath = Path.Combine(homeDirectory, "dalamudConfig.json");
    }

    public string HomeDirectory { get; }

    public string ConfigurationDirectory { get; }

    public string InstalledPluginsDirectory { get; }

    public string PluginConfigurationsDirectory { get; }

    public string LogDirectory { get; }

    public string ImportDirectory { get; }

    public string TempDirectory { get; }

    public string StandaloneConfigurationPath { get; }

    public string DalamudConfigurationPath { get; }

    public static StandalonePaths Create()
    {
        return new StandalonePaths(ResolveHome(AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetEnvironmentVariable("MOONWAKE_HOME")));
    }

    internal static string ResolveHome(string executableDirectory, string applicationDataDirectory, string? configuredHome)
    {
        if (!string.IsNullOrWhiteSpace(configuredHome))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredHome.Trim()));

        var profilePath = Path.Combine(executableDirectory, "moonwake.profile.json");
        if (!File.Exists(profilePath)) return Path.Combine(applicationDataDirectory, "Moonwake");
        try
        {
            using var profile = JsonDocument.Parse(File.ReadAllText(profilePath));
            if (profile.RootElement.ValueKind != JsonValueKind.Object || !profile.RootElement.TryGetProperty("homeDirectory", out var home) || home.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(home.GetString()))
                throw new InvalidDataException("moonwake.profile.json 必须包含 homeDirectory 数据目录。");
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(home.GetString()!.Trim()), executableDirectory);
        }
        catch (JsonException ex) { throw new InvalidDataException("moonwake.profile.json 格式无效，请检查启动配置。", ex); }
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(this.HomeDirectory);
        Directory.CreateDirectory(this.ConfigurationDirectory);
        Directory.CreateDirectory(this.InstalledPluginsDirectory);
        Directory.CreateDirectory(this.PluginConfigurationsDirectory);
        Directory.CreateDirectory(this.LogDirectory);
        Directory.CreateDirectory(this.ImportDirectory);
        Directory.CreateDirectory(this.TempDirectory);
    }

    public PayloadLayout ResolvePayloadLayout(StandaloneConfiguration configuration)
    {
        var payloadDirectory = this.ResolveConfiguredPath(configuration.PayloadDirectory);
        var assetDirectory = this.ResolveConfiguredPath(configuration.AssetDirectory);
        var runtimeDirectory = this.ResolveConfiguredPath(configuration.RuntimeDirectory);

        return new PayloadLayout(
            payloadDirectory,
            Path.Combine(payloadDirectory, "Dalamud.Injector.exe"),
            Path.Combine(payloadDirectory, "Dalamud.Boot.dll"),
            Path.Combine(payloadDirectory, "Dalamud.dll"),
            assetDirectory,
            runtimeDirectory);
    }

    public void EnsureConfiguredDirectories(StandaloneConfiguration configuration)
    {
        var layout = this.ResolvePayloadLayout(configuration);
        Directory.CreateDirectory(layout.PayloadDirectory);
        Directory.CreateDirectory(layout.AssetDirectory);
        Directory.CreateDirectory(layout.RuntimeDirectory);
    }

    private string ResolveConfiguredPath(string configuredPath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
        return Path.GetFullPath(
            Path.IsPathFullyQualified(expanded)
                ? expanded
                : Path.Combine(this.HomeDirectory, expanded));
    }
}

internal sealed record PayloadLayout(
    string PayloadDirectory,
    string InjectorPath,
    string BootPath,
    string DalamudPath,
    string AssetDirectory,
    string RuntimeDirectory);
