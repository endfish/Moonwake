using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Services;

internal static class DistributionTests
{
    public static int Run(string root)
    {
        var paths = StandalonePaths.ForRoot(Path.Combine(root, "fresh-roaming", "XIVLauncherCN"));
        var files = new Dictionary<string, string>
        {
            ["payloads/test/Dalamud.dll"] = "core fixture",
            ["assets/test/font.bin"] = "asset fixture",
            ["runtime/test/runtime.bin"] = "runtime fixture",
        };
        var count = 0;
        {
            var config = Prepare();
            Check(File.ReadAllText(Path.Combine(paths.HomeDirectory, "payloads/test/Dalamud.dll")) == "core fixture" &&
                config.PayloadDirectory == "payloads/test" && Directory.Exists(paths.InstalledPluginsDirectory) &&
                !Directory.Exists(Path.Combine(root, "fresh-roaming", "Moonwake")), "first launch creates XIVLauncherCN with plugin directories and bundled resources, without a sibling AppData/Moonwake directory");
            var sentinel = Path.Combine(paths.PluginConfigurationsDirectory, "Personal.json");
            File.WriteAllText(sentinel, "personal settings must survive");
            config.LoadPluginsByDefault = false;
            config.InitializationDelayMs = 4321;
            new ConfigurationStore(paths).Save(config);
            var settings = new JsonObject
            {
                ["PluginSafeMode"] = false,
                ["ThirdRepoList"] = new JsonArray(new JsonObject { ["Url"] = "https://example.test/user" }),
                ["KeepMe"] = 42,
            };
            File.WriteAllText(paths.DalamudConfigurationPath, settings.ToJsonString());
            var originalGlobal = File.ReadAllText(paths.DalamudConfigurationPath);
            config = Prepare();
            Check(!config.LoadPluginsByDefault && config.InitializationDelayMs == 4321 &&
                File.ReadAllText(paths.DalamudConfigurationPath) == originalGlobal && File.ReadAllText(sentinel) == "personal settings must survive",
                "repeat startup preserves user preferences, repositories and plugin configuration");
            File.WriteAllText(Path.Combine(paths.HomeDirectory, "payloads/test/Dalamud.dll"), "damaged");
            config = Prepare();
            Check(config.PayloadDirectory.StartsWith("payloads/test-repair-") &&
                File.ReadAllText(Path.Combine(paths.HomeDirectory, "payloads/test/Dalamud.dll")) == "damaged" &&
                File.ReadAllText(Path.Combine(paths.HomeDirectory, config.PayloadDirectory, "Dalamud.dll")) == "core fixture",
                "damaged resource repair stages a new copy without deleting existing data");
            files["payloads/test/Dalamud.dll"] = "new release";
            var beforeFailure = File.ReadAllText(paths.StandaloneConfigurationPath);
            try { Prepare(corrupt: true); throw new Exception("Expected integrity failure"); }
            catch (InvalidDataException) { }
            Check(File.ReadAllText(paths.StandaloneConfigurationPath) == beforeFailure && File.ReadAllText(sentinel) == "personal settings must survive",
                "failed package verification cannot replace the selected release or user data");
            files["../outside"] = "invalid";
            try { Prepare(); throw new Exception("Expected traversal failure"); }
            catch (InvalidDataException) { }
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(paths.HomeDirectory)!, "outside")), "package traversal is rejected");
            files.Remove("../outside");

            return count;
        }

        StandaloneConfiguration Prepare(bool corrupt = false)
        {
            var hashes = files.ToDictionary(item => item.Key, item => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Value))));
            var manifest = new DistributionManifest(1, "test", "payloads/test", "assets/test", "runtime/test", ["2026.10.06.0000.0000"], hashes);
            using var manifestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest));
            using var archiveStream = new MemoryStream();
            using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Create, true))
                foreach (var file in files)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(file.Key).Open());
                    writer.Write(file.Value + (corrupt ? "bad" : ""));
                }
            archiveStream.Position = 0;
            return DistributionBootstrap.Prepare(paths, manifestStream, archiveStream);
        }

        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            count++;
            Console.WriteLine("PASS: " + description);
        }
    }
}
