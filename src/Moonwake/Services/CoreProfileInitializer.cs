using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moonwake.Infrastructure;

namespace Moonwake.Services;

internal static class CoreProfileInitializer
{
    // Only plugin registration and loading state are inherited. Core preferences stay at Moonwake defaults.
    private static readonly string[] InheritedFields =
    [
        "ThirdRepoList", "ThirdRepoSpeedbumpDismissed", "DevPluginLoadLocations", "DevPluginSettings", "DevMode",
        "SavedProfiles", "DefaultProfile", "ProfilesEnabled", "ProfilesHasSeenTutorial", "DoPluginTest", "PluginTestingOptIns",
    ];

    public static void EnsureCreated(StandalonePaths paths)
    {
        if (File.Exists(paths.DalamudConfigurationPath))
        {
            using var existing = ReadObject(paths.DalamudConfigurationPath);
            return;
        }

        using var source = File.Exists(paths.CnConfigurationPath) ? ReadObject(paths.CnConfigurationPath) : JsonDocument.Parse("{}");
        var profile = new JsonObject();
        foreach (var field in InheritedFields)
            if (source.RootElement.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null)
                profile[field] = JsonNode.Parse(value.GetRawText());
        profile["LanguageOverride"] = "zh";
        profile["ChosenStyle"] = "Moonwake Blue";
        profile["PluginSafeMode"] = false;
        Directory.CreateDirectory(paths.CoreConfigurationDirectory);
        var pending = Path.Combine(paths.CoreConfigurationDirectory, ".initial-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(pending, profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(pending, paths.DalamudConfigurationPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(paths.DalamudConfigurationPath))
        {
            using var existing = ReadObject(paths.DalamudConfigurationPath);
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
        }
    }

    private static JsonDocument ReadObject(string path)
    {
        try
        {
            var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
            throw new JsonException("Expected a configuration object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"无法读取配置文件，已保留原文件：{path}", ex);
        }
    }
}
