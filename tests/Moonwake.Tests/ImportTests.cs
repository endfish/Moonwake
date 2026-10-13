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
        const string wrappedSource = """
            {"ThirdRepoList":{"$type":"System.Collections.Generic.List`1[[Dalamud.Configuration.ThirdPartyRepoSettings, Dalamud]], System.Private.CoreLib","$values":[{"Url":"https://example.test/wrapped.json","IsEnabled":true}]}}
            """;
        File.WriteAllText(Path.Combine(source, "dalamudConfig.json"), wrappedSource);
        var wrappedPlan = service.Inspect(source);
        Check(wrappedPlan.Repositories.SequenceEqual(["https://example.test/wrapped.json"]), "repository preview reads Dalamud type-wrapped collections");
        File.WriteAllText(paths.DalamudConfigurationPath, wrappedSource);
        result = service.Import(plan, false, [repo]);
        var wrappedGlobal = JsonNode.Parse(File.ReadAllText(paths.DalamudConfigurationPath))!;
        Check(result.Repositories == 1 && wrappedGlobal["ThirdRepoList"]!["$values"]!.AsArray().Count == 2 &&
              wrappedGlobal["ThirdRepoList"]!["$type"]!.GetValue<string>().Contains("ThirdPartyRepoSettings"), "repository import preserves Dalamud collection metadata and existing entries");
        Check(service.Import(plan, false, [repo]).Repositories == 0, "wrapped repository merge is idempotent");
        File.WriteAllText(paths.DalamudConfigurationPath, "{\"ThirdRepoList\":{\"$values\":false}}");
        Expect(() => service.Import(plan, false, [repo]), "malformed repository metadata is rejected before committing data");
        File.WriteAllText(paths.DalamudConfigurationPath, wrappedGlobal.ToJsonString());
        Expect(() => service.Import(plan, false, ["https://unselected.test/repo.json"]), "unpreviewed repository selection is rejected");
        Expect(() => service.Inspect(paths.HomeDirectory), "shared or overlapping source and target directories are rejected");

        var devDirectory = Path.Combine(source, "development-output");
        Directory.CreateDirectory(devDirectory);
        var devDll = Path.Combine(devDirectory, "Developer.dll");
        File.WriteAllText(devDll, "development fixture; never executed");
        File.WriteAllText(Path.ChangeExtension(devDll, ".json"), "{\"InternalName\":\"Developer\",\"DalamudApiLevel\":15}");
        File.WriteAllText(Path.Combine(source, "pluginConfigs", "Developer.json"), "{\"fromDevelopment\":true}");
        var devSource = new JsonObject
        {
            ["DevPluginLoadLocations"] = new JsonObject { ["$values"] = new JsonArray
            {
                new JsonObject { ["Path"] = devDll.Replace('\\', '/'), ["IsEnabled"] = true, ["Nickname"] = "Work in progress" },
                new JsonObject { ["Path"] = devDll, ["IsEnabled"] = true },
                new JsonObject { ["Path"] = Path.Combine(devDirectory, "Missing.dll"), ["IsEnabled"] = true },
            } },
            ["DevPluginSettings"] = new JsonObject { [devDll] = new JsonObject
            {
                ["StartOnBoot"] = true, ["AutomaticReloading"] = true, ["NotifyForErrors"] = false, ["WorkingPluginId"] = originalId,
            } },
        };
        var devSourceText = devSource.ToJsonString();
        File.WriteAllText(Path.Combine(source, "dalamudConfig.json"), devSourceText);
        var devPlan = service.Inspect(source);
        Check(devPlan.DevelopmentPlugins.Count == 1 && devPlan.SkippedDevelopmentPlugins == 1, "development preview normalizes duplicate paths and skips missing DLLs");
        Check(service.Import(devPlan, true, []).DevelopmentPlugins == 0, "development paths require the development import option");
        result = service.Import(devPlan, true, [], true);
        var devGlobal = JsonNode.Parse(File.ReadAllText(paths.DalamudConfigurationPath))!;
        var devSetting = devGlobal["DevPluginSettings"]![devDll]!;
        Check(result.DevelopmentPlugins == 1 && result.ConfigurationItems == 1 && devGlobal["DevMode"]!.GetValue<bool>() &&
              !devSetting["StartOnBoot"]!.GetValue<bool>() && devSetting["AutomaticReloading"]!.GetValue<bool>() &&
              !devSetting["NotifyForErrors"]!.GetValue<bool>() && devSetting["WorkingPluginId"]!.GetValue<string>() != originalId,
              "development import preserves reload preferences with independent identity and startup disabled");
        Check(devGlobal["DevPluginLoadLocations"]![0]!["Path"]!.GetValue<string>() == devDll &&
              File.ReadAllText(Path.Combine(paths.PluginConfigurationsDirectory, "Developer.json")).Contains("fromDevelopment") &&
              File.ReadAllText(Path.Combine(source, "dalamudConfig.json")) == devSourceText && File.ReadAllText(devDll).Contains("never executed"),
              "development import references the original build output and copies configuration without changing the source");
        var savedDevConfiguration = File.ReadAllText(paths.DalamudConfigurationPath);
        Check(service.Import(devPlan, true, [], true).DevelopmentPlugins == 0 && File.ReadAllText(paths.DalamudConfigurationPath) == savedDevConfiguration,
              "repeated development import preserves settings and identities");

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
