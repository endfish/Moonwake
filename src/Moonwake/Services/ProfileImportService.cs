using System.Text.Json;
using System.Text.Json.Nodes;
using Moonwake.Infrastructure;

namespace Moonwake.Services;

internal sealed record ImportPlugin(string Name, string VersionDirectory);
internal sealed record ImportDevPlugin(string Path, string Name, string? Nickname, bool IsEnabled, bool NotifyForErrors, bool AutomaticReloading);
internal sealed record ImportPlan(string SourceRoot, IReadOnlyList<ImportPlugin> Plugins, IReadOnlyList<string> Repositories, int SkippedPlugins, IReadOnlyList<ImportDevPlugin> DevelopmentPlugins, int SkippedDevelopmentPlugins);
internal sealed record ImportResult(int Plugins, int ConfigurationItems, int Repositories, int ExistingPlugins, int DevelopmentPlugins);

/// <summary>Copies selected data into an isolated profile; never executes or edits source plugins.</summary>
internal sealed class ProfileImportService(StandalonePaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ImportPlan Inspect(string source)
    {
        var root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        RejectOverlap(root, paths.HomeDirectory);
        RejectLinks(root);
        var pluginRoot = Path.Combine(root, "installedPlugins");
        if (!Directory.Exists(pluginRoot)) throw new InvalidDataException("所选目录中没有 installedPlugins 文件夹。请选择原环境的数据根目录。");
        RejectLinks(pluginRoot);
        var plugins = new List<ImportPlugin>();
        var skipped = 0;
        foreach (var directory in Directory.EnumerateDirectories(pluginRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            RejectLinks(directory);
            var name = Path.GetFileName(directory);
            var candidates = new List<(Version Version, string Directory)>();
            foreach (var versionDirectory in Directory.EnumerateDirectories(directory))
            {
                RejectLinks(versionDirectory);
                var manifestPath = Path.Combine(versionDirectory, name + ".json");
                var dll = Path.Combine(versionDirectory, name + ".dll");
                if (!File.Exists(manifestPath) || !File.Exists(dll) || !Version.TryParse(Path.GetFileName(versionDirectory), out var version)) continue;
                RejectLinks(manifestPath);
                RejectLinks(dll);
                try
                {
                    var manifest = ReadObject(manifestPath);
                    var api = manifest["Testing"]?.GetValue<bool>() == true
                        ? manifest["TestingDalamudApiLevel"]?.GetValue<int>() ?? manifest["DalamudApiLevel"]?.GetValue<int>()
                        : manifest["DalamudApiLevel"]?.GetValue<int>();
                    if (manifest["InternalName"]?.GetValue<string>() != name || api != 15 || manifest["ScheduledForDeletion"]?.GetValue<bool>() == true) continue;
                    candidates.Add((version, versionDirectory));
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
            }
            if (candidates.Count == 0) { skipped++; continue; }
            plugins.Add(new ImportPlugin(name, candidates.MaxBy(x => x.Version).Directory));
        }

        var repositories = new List<string>();
        var developmentPlugins = new List<ImportDevPlugin>();
        var skippedDevelopment = 0;
        var configurationPath = Path.Combine(root, "dalamudConfig.json");
        if (File.Exists(configurationPath))
        {
            RejectLinks(configurationPath);
            var config = ReadObject(configurationPath);
            if (RepositoryArray(config) is { } repos)
                foreach (var item in repos.OfType<JsonObject>())
                {
                    var url = item["Url"]?.GetValue<string>();
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
                        repositories.Add(url!);
                }
            if (ConfigurationArray(config, "DevPluginLoadLocations") is { } locations)
                foreach (var location in locations.OfType<JsonObject>())
                {
                    var path = location["Path"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    { skippedDevelopment++; continue; }
                    path = Path.GetFullPath(path);
                    var manifestPath = Path.ChangeExtension(path, ".json");
                    if (!File.Exists(manifestPath)) { skippedDevelopment++; continue; }
                    RejectLinks(path);
                    RejectLinks(manifestPath);
                    var manifest = ReadObject(manifestPath);
                    var name = manifest["InternalName"]?.GetValue<string>();
                    if (manifest["DalamudApiLevel"]?.GetValue<int>() != 15 || string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
                    { skippedDevelopment++; continue; }
                    if (developmentPlugins.Any(plugin => string.Equals(plugin.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                    var settings = (config["DevPluginSettings"] as JsonObject)?.FirstOrDefault(item =>
                        item.Key != "$type" && Path.IsPathFullyQualified(item.Key) && string.Equals(Path.GetFullPath(item.Key), path, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
                    developmentPlugins.Add(new ImportDevPlugin(path, name, location["Nickname"]?.GetValue<string>(), location["IsEnabled"]?.GetValue<bool>() ?? true,
                        settings?["NotifyForErrors"]?.GetValue<bool>() ?? true, settings?["AutomaticReloading"]?.GetValue<bool>() ?? false));
                }
        }
        return new ImportPlan(root, plugins, repositories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), skipped, developmentPlugins, skippedDevelopment);
    }

    public ImportResult Import(ImportPlan plan, bool includeConfiguration, IReadOnlyCollection<string> selectedRepositories, bool includeDevelopmentPlugins = false)
    {
        RejectOverlap(plan.SourceRoot, paths.HomeDirectory);
        if (selectedRepositories.Any(url => !plan.Repositories.Contains(url, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("导入选择包含未预览的仓库地址。");
        paths.EnsureDirectories();
        RejectLinks(paths.HomeDirectory);
        RejectLinks(paths.InstalledPluginsDirectory);
        RejectLinks(paths.PluginConfigurationsDirectory);
        using var importLock = new FileStream(Path.Combine(paths.HomeDirectory, ".import.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var stage = Path.Combine(paths.ImportDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var moves = new List<(string Source, string Destination, bool Directory)>();
        var pluginCount = 0;
        var configurationCount = 0;
        var existing = 0;
        foreach (var plugin in plan.Plugins)
        {
            var destination = Path.Combine(paths.InstalledPluginsDirectory, plugin.Name);
            if (Directory.Exists(destination)) { existing++; continue; }
            var stagedRoot = Path.Combine(stage, "installedPlugins", plugin.Name);
            var stagedVersion = Path.Combine(stagedRoot, Path.GetFileName(plugin.VersionDirectory));
            CopyTree(plugin.VersionDirectory, stagedVersion);
            var manifestPath = Path.Combine(stagedVersion, plugin.Name + ".json");
            var manifest = ReadObject(manifestPath);
            // New identity avoids reusing a source collection entry; the core migrates Disabled into its default collection.
            manifest["WorkingPluginId"] = Guid.NewGuid().ToString();
            manifest["Disabled"] = true;
            manifest["ScheduledForDeletion"] = false;
            File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions));
            moves.Add((stagedRoot, destination, true));
            pluginCount++;
            if (!includeConfiguration) continue;
            StageConfiguration(plugin.Name);
        }

        void StageConfiguration(string pluginName)
        {
            var sourceConfigs = Path.Combine(plan.SourceRoot, "pluginConfigs");
            if (Directory.Exists(sourceConfigs)) RejectLinks(sourceConfigs);
            foreach (var name in new[] { pluginName, pluginName + ".json" })
            {
                var from = Path.Combine(sourceConfigs, name);
                var to = Path.Combine(paths.PluginConfigurationsDirectory, name);
                if (Directory.Exists(to) || File.Exists(to) || moves.Any(move => string.Equals(move.Destination, to, StringComparison.OrdinalIgnoreCase))) continue;
                var staged = Path.Combine(stage, "pluginConfigs", name);
                if (Directory.Exists(from)) { CopyTree(from, staged); moves.Add((staged, to, true)); configurationCount++; }
                else if (File.Exists(from))
                {
                    RejectLinks(sourceConfigs);
                    RejectLinks(from);
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                    File.Copy(from, staged);
                    moves.Add((staged, to, false));
                    configurationCount++;
                }
            }
        }

        var globalPath = paths.DalamudConfigurationPath;
        var original = File.Exists(globalPath) ? File.ReadAllText(globalPath) : null;
        var global = original is null ? new JsonObject() : JsonNode.Parse(original)?.AsObject() ?? throw new InvalidDataException("Moonwake 配置无效。");
        var repositories = RepositoryArray(global) ?? new JsonArray();
        if (global["ThirdRepoList"] is null) global["ThirdRepoList"] = repositories;
        var addedRepos = 0;
        foreach (var url in selectedRepositories)
        {
            if (repositories.OfType<JsonObject>().Any(item => string.Equals(item["Url"]?.GetValue<string>(), url, StringComparison.OrdinalIgnoreCase))) continue;
            repositories.Add(new JsonObject { ["Url"] = url, ["IsEnabled"] = true });
            addedRepos++;
        }
        var addedDevelopment = 0;
        if (includeDevelopmentPlugins && plan.DevelopmentPlugins.Count > 0)
        {
            var locations = ConfigurationArray(global, "DevPluginLoadLocations") ?? new JsonArray();
            if (global["DevPluginLoadLocations"] is null) global["DevPluginLoadLocations"] = locations;
            var settings = global["DevPluginSettings"] as JsonObject ?? new JsonObject();
            if (global["DevPluginSettings"] is null) global["DevPluginSettings"] = settings;
            foreach (var plugin in plan.DevelopmentPlugins)
            {
                if (locations.OfType<JsonObject>().Any(item => item["Path"]?.GetValue<string>() is { } path &&
                    Path.IsPathFullyQualified(path) && string.Equals(Path.GetFullPath(path), plugin.Path, StringComparison.OrdinalIgnoreCase))) continue;
                locations.Add(new JsonObject { ["Path"] = plugin.Path, ["IsEnabled"] = plugin.IsEnabled, ["Nickname"] = plugin.Nickname });
                settings[plugin.Path] = new JsonObject
                {
                    ["StartOnBoot"] = false,
                    ["NotifyForErrors"] = plugin.NotifyForErrors,
                    ["AutomaticReloading"] = plugin.AutomaticReloading,
                    ["WorkingPluginId"] = Guid.NewGuid().ToString(),
                };
                if (includeConfiguration) StageConfiguration(plugin.Name);
                addedDevelopment++;
            }
            if (addedDevelopment > 0) global["DevMode"] = true;
        }
        var applied = new List<(string Source, string Destination, bool Directory)>();
        var writeConfig = addedRepos > 0 || addedDevelopment > 0;
        if (writeConfig && original is not null) File.WriteAllText(Path.Combine(stage, "dalamudConfig.before.json"), original);
        try
        {
            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
                if (move.Directory) Directory.Move(move.Source, move.Destination);
                else File.Move(move.Source, move.Destination);
                applied.Add(move);
            }
            if (writeConfig)
            {
                var temporary = Path.Combine(stage, "dalamudConfig.new.json");
                File.WriteAllText(temporary, global.ToJsonString(JsonOptions));
                File.Move(temporary, globalPath, overwrite: true);
            }
        }
        catch
        {
            foreach (var move in applied.AsEnumerable().Reverse())
            {
                if (move.Directory) Directory.Move(move.Destination, move.Source);
                else File.Move(move.Destination, move.Source);
            }
            throw;
        }
        return new ImportResult(pluginCount, configurationCount, addedRepos, existing, addedDevelopment);
    }

    private static JsonObject ReadObject(string file) => JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("配置内容为空。");

    private static JsonArray? RepositoryArray(JsonObject configuration) => ConfigurationArray(configuration, "ThirdRepoList");

    private static JsonArray? ConfigurationArray(JsonObject configuration, string key) => configuration[key] switch
    {
        null => null,
        JsonArray array => array,
        JsonObject wrapper when wrapper["$values"] is JsonArray array => array,
        _ => throw new InvalidDataException($"配置列表 {key} 格式无效。"),
    };

    private static void CopyTree(string source, string destination)
    {
        RejectLinks(source);
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            RejectLinks(entry);
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyTree(entry, target);
            else File.Copy(entry, target);
        }
    }

    private static void RejectLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("导入不接受符号链接或目录联接，请选择实际数据目录。");
    }

    private static void RejectOverlap(string first, string second)
    {
        first = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        second = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (first.StartsWith(second, StringComparison.OrdinalIgnoreCase) || second.StartsWith(first, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("导入源与 Moonwake 数据目录不能相同或互相包含。");
    }
}
