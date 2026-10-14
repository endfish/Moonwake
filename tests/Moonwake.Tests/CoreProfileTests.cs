using System.Text.Json;
using System.Text.Json.Nodes;
using Moonwake.Infrastructure;
using Moonwake.Services;

internal static class CoreProfileTests
{
    public static int Run(string root)
    {
        var count = 0;
        var fresh = StandalonePaths.ForRoot(Path.Combine(root, "profile-fresh"));
        CoreProfileInitializer.EnsureCreated(fresh);
        var config = Read(fresh.DalamudConfigurationPath);
        Check(config["ChosenStyle"]!.GetValue<string>() == "Moonwake Blue" &&
              config["LanguageOverride"]!.GetValue<string>() == "zh" && !config["PluginSafeMode"]!.GetValue<bool>() &&
              !File.Exists(fresh.CnConfigurationPath), "fresh profile uses Chinese, blue appearance and normal plugin loading without creating CN core settings");

        var existing = StandalonePaths.ForRoot(Path.Combine(root, "profile-existing"));
        existing.EnsureDirectories();
        var original = """
            {"ChosenStyle":"CN Theme","GlobalUiScale":2.0,"PluginSafeMode":true,"IsMbCollect":true,
             "EffectiveLanguage":"zh","EffectiveLanguage":"zh",
             "ThirdRepoList":{"$values":[{"Url":"https://example.test/repo","IsEnabled":false}]},
             "DevPluginLoadLocations":{"$values":[{"Path":"D:/work/example.dll","IsEnabled":true}]},
             "DevPluginSettings":{"D:/work/example.dll":{"WorkingPluginId":"test-id","StartOnBoot":false,"AutomaticReloading":true}},
             "DefaultProfile":{"pluginState":"preserve"},"SavedProfiles":{"$values":[]},"ProfilesEnabled":true}
            """;
        File.WriteAllText(existing.CnConfigurationPath, original);
        var sentinel = Path.Combine(existing.PluginConfigurationsDirectory, "Example.json");
        File.WriteAllText(sentinel, "existing plugin settings");
        CoreProfileInitializer.EnsureCreated(existing);
        config = Read(existing.DalamudConfigurationPath);
        using var source = JsonDocument.Parse(original);
        Check(new[] { "ThirdRepoList", "DevPluginLoadLocations", "DevPluginSettings", "DefaultProfile", "SavedProfiles", "ProfilesEnabled" }
            .All(key => JsonNode.DeepEquals(JsonNode.Parse(source.RootElement.GetProperty(key).GetRawText()), config[key])), "first startup tolerates duplicate unrelated fields and inherits plugin registration exactly");
        Check(config["ChosenStyle"]!.GetValue<string>() == "Moonwake Blue" && config["GlobalUiScale"] is null &&
              config["IsMbCollect"] is null && !config["PluginSafeMode"]!.GetValue<bool>(), "CN appearance and core behavior are not inherited");
        config["ChosenStyle"] = "Personal Theme";
        config["ThirdRepoList"] = new JsonArray();
        File.WriteAllText(existing.DalamudConfigurationPath, config.ToJsonString());
        var moonwakeBefore = File.ReadAllText(existing.DalamudConfigurationPath);
        File.WriteAllText(existing.CnConfigurationPath, "{\"ThirdRepoList\":[{\"Url\":\"https://example.test/new\"}]}");
        var cnBefore = File.ReadAllText(existing.CnConfigurationPath);
        CoreProfileInitializer.EnsureCreated(existing);
        Check(File.ReadAllText(existing.DalamudConfigurationPath) == moonwakeBefore &&
              File.ReadAllText(existing.CnConfigurationPath) == cnBefore && File.ReadAllText(sentinel) == "existing plugin settings",
              "later startups do not resync repositories, replace Moonwake preferences or alter CN/plugin data");

        var malformed = StandalonePaths.ForRoot(Path.Combine(root, "profile-malformed"));
        malformed.EnsureDirectories();
        File.WriteAllText(malformed.CnConfigurationPath, "{broken");
        try { CoreProfileInitializer.EnsureCreated(malformed); throw new Exception("Expected invalid input failure."); }
        catch (InvalidDataException) { }
        Check(!File.Exists(malformed.DalamudConfigurationPath) && File.ReadAllText(malformed.CnConfigurationPath) == "{broken",
            "unreadable source cannot create an empty replacement profile");
        File.WriteAllText(malformed.DalamudConfigurationPath, "{damaged Moonwake");
        try { CoreProfileInitializer.EnsureCreated(malformed); throw new Exception("Expected existing profile failure."); }
        catch (InvalidDataException) { }
        Check(File.ReadAllText(malformed.DalamudConfigurationPath) == "{damaged Moonwake", "invalid existing Moonwake configuration is preserved for recovery");

        if (Environment.GetEnvironmentVariable("MOONWAKE_CN_PROFILE_FIXTURE") is { Length: > 0 } fixture)
        {
            var real = StandalonePaths.ForRoot(Path.Combine(root, "profile-fixture"));
            real.EnsureDirectories();
            File.Copy(fixture, real.CnConfigurationPath);
            CoreProfileInitializer.EnsureCreated(real);
            using var before = JsonDocument.Parse(File.ReadAllText(real.CnConfigurationPath));
            var after = Read(real.DalamudConfigurationPath);
            Check(new[] { "ThirdRepoList", "DevPluginLoadLocations", "DevPluginSettings", "DefaultProfile", "SavedProfiles" }
                .All(key => JsonNode.DeepEquals(JsonNode.Parse(before.RootElement.GetProperty(key).GetRawText()), after[key])), "real profile fixture retains plugin registration and loading state");
            if (Environment.GetEnvironmentVariable("MOONWAKE_INITIALIZED_PROFILE_OUTPUT") is { Length: > 0 } output)
                File.Copy(real.DalamudConfigurationPath, output, overwrite: true);
        }
        return count;

        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            count++;
            Console.WriteLine("PASS: " + description);
        }
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}
