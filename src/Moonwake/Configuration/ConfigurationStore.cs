using System.Text.Json;

using Moonwake.Infrastructure;

namespace Moonwake.Configuration;

internal sealed class ConfigurationStore(StandalonePaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public StandaloneConfiguration LoadOrCreate()
    {
        if (!File.Exists(paths.StandaloneConfigurationPath))
        {
            var created = new StandaloneConfiguration();
            created.Normalize();
            this.Save(created);
            return created;
        }

        try
        {
            var json = File.ReadAllText(paths.StandaloneConfigurationPath);
            var configuration = JsonSerializer.Deserialize<StandaloneConfiguration>(json, JsonOptions)
                                ?? throw new InvalidDataException("配置文件反序列化结果为空。");
            configuration.Normalize();
            return configuration;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"配置文件 JSON 无效：{paths.StandaloneConfigurationPath}", ex);
        }
    }

    public void Save(StandaloneConfiguration configuration)
    {
        configuration.Normalize();
        Directory.CreateDirectory(paths.ConfigurationDirectory);

        var temporaryPath = paths.StandaloneConfigurationPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration, JsonOptions));
        File.Move(temporaryPath, paths.StandaloneConfigurationPath, true);
    }
}
