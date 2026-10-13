using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moonwake.Infrastructure;
using Moonwake.Services;

internal static class ImportTests
{
    public static int Run(StandalonePaths paths)
    {
        var passed = 0;
        var source = Path.Combine(Path.GetDirectoryName(paths.HomeDirectory)!, Guid.NewGuid().ToString("N") + "-source");
        var sourcePlugin = Path.Combine(source, "installedPlugins", "Example", "1.2.3");
        Directory.CreateDirectory(sourcePlugin);
        File.WriteAllText(Path.Combine(sourcePlugin, "Example.dll"), "test fixture only; never executed");
        var originalId = Guid.NewGuid().ToString();
        var manifestPath = Path.Combine(sourcePlugin, "Example.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { InternalName = "Example", DalamudApiLevel = 15, Disabled = false, WorkingPluginId = originalId }));
        var originalHash = SHA256.HashData(File.ReadAllBytes(manifestPath));
        var incompatible = Path.Combine(source, "installedPlugins", "Outdated", "1.0.0");
        Directory.CreateDirectory(incompatible);
        File.WriteAllText(Path.Combine(incompatible, "Outdated.dll"), "test fixture only");
        File.WriteAllText(Path.Combine(incompatible, "Outdated.json"), "{\"InternalName\":\"Outdated\",\"DalamudApiLevel\":14}");
        Directory.CreateDirectory(Path.Combine(source, "pluginConfigs", "Example"));
        File.WriteAllText(Path.Combine(source, "pluginConfigs", "Example.json"), "{\"setting\":42}");
        File.WriteAllText(Path.Combine(source, "pluginConfigs", "Example", "presets.json"), "[]");
        const string repo = "https://example.test/repo.json";
        File.WriteAllText(Path.Combine(source, "dalamudConfig.json"), JsonSerializer.Serialize(new { IsMbCollect = true, ThirdRepoList = new[] { new { Url = repo, IsEnabled = true } } }));
        var service = new ProfileImportService(paths);
        var plan = service.Inspect(source);
        Check(plan.Plugins.Count == 1 && plan.SkippedPlugins == 1, "import skips incompatible API versions");
        var result = service.Import(plan, true, []);
        var importedManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(paths.InstalledPluginsDirectory, "Example", "1.2.3", "Example.json")))!;
        Check(result.Plugins == 1 && result.ConfigurationItems == 2 && importedManifest["Disabled"]!.GetValue<bool>() &&
              importedManifest["WorkingPluginId"]!.GetValue<string>() != originalId, "copied plugins get a disabled independent identity");
        Check(SHA256.HashData(File.ReadAllBytes(manifestPath)).SequenceEqual(originalHash), "source plugin manifest is unchanged");
        Check(!File.Exists(paths.DalamudConfigurationPath), "unselected repositories and source global settings are not imported");
        var targetConfiguration = Path.Combine(paths.PluginConfigurationsDirectory, "Example.json");
        File.WriteAllText(targetConfiguration, "{\"setting\":99}");
        result = service.Import(plan, true, []);
        Check(result.Plugins == 0 && result.ExistingPlugins == 1 && File.ReadAllText(targetConfiguration).Contains("99"), "repeated import preserves existing plugins and configuration");
        File.WriteAllText(paths.DalamudConfigurationPath, "{\"IsMbCollect\":false,\"CustomPreference\":\"keep\"}");
        result = service.Import(plan, false, [repo]);
        var targetGlobal = JsonNode.Parse(File.ReadAllText(paths.DalamudConfigurationPath))!;
        Check(result.Repositories == 1 && targetGlobal["ThirdRepoList"]!.AsArray().Count == 1 &&
              !targetGlobal["IsMbCollect"]!.GetValue<bool>() && targetGlobal["CustomPreference"]!.GetValue<string>() == "keep", "only selected repositories merge into existing global configuration");
        Check(service.Import(plan, false, [repo]).Repositories == 0, "repository merge is idempotent");
        Expect(() => service.Import(plan, false, ["https://unselected.test/repo.json"]), "unpreviewed repository selection is rejected");
        Expect(() => service.Inspect(paths.HomeDirectory), "shared or overlapping source and target directories are rejected");

        var blocked = Path.Combine(source, "installedPlugins", "Blocked", "1.0.0");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "Blocked.dll"), "fixture");
        File.WriteAllText(Path.Combine(blocked, "Blocked.json"), "{\"InternalName\":\"Blocked\",\"DalamudApiLevel\":15}");
        using (var lockedFile = new FileStream(Path.Combine(blocked, "Blocked.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Expect(() => service.Import(service.Inspect(source), true, []), "unreadable source aborts import before committing files");
        }
        Check(!Directory.Exists(Path.Combine(paths.InstalledPluginsDirectory, "Blocked")), "failed staging leaves target plugin directory unchanged");
        return passed;

        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            passed++;
            Console.WriteLine("PASS: " + description);
        }

        void Expect(Action action, string description)
        {
            try { action(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Check(true, description); return; }
            throw new Exception("FAIL: " + description);
        }
    }
}
