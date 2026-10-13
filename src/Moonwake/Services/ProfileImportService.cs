using System.Text.Json;
using System.Text.Json.Nodes;
using Moonwake.Infrastructure;

namespace Moonwake.Services;

internal sealed record ImportPlugin(string Name, string VersionDirectory);
internal sealed record ImportPlan(string SourceRoot, IReadOnlyList<ImportPlugin> Plugins, IReadOnlyList<string> Repositories, int SkippedPlugins);
internal sealed record ImportResult(int Plugins, int ConfigurationItems, int Repositories, int ExistingPlugins);

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
        }
        return new ImportPlan(root, plugins, repositories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), skipped);
    }

    public ImportResult Import(ImportPlan plan, bool includeConfiguration, IReadOnlyCollection<string> selectedRepositories)
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
            var sourceConfigs = Path.Combine(plan.SourceRoot, "pluginConfigs");
            if (Directory.Exists(sourceConfigs)) RejectLinks(sourceConfigs);
            foreach (var name in new[] { plugin.Name, plugin.Name + ".json" })
            {
                var from = Path.Combine(sourceConfigs, name);
                var to = Path.Combine(paths.PluginConfigurationsDirectory, name);
                if (Directory.Exists(to) || File.Exists(to)) continue;
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
        var applied = new List<(string Source, string Destination, bool Directory)>();
        var writeConfig = addedRepos > 0;
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
        return new ImportResult(pluginCount, configurationCount, addedRepos, existing);
    }

    private static JsonObject ReadObject(string file) => JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("配置内容为空。");

    private static JsonArray? RepositoryArray(JsonObject configuration) => configuration["ThirdRepoList"] switch
    {
        null => null,
        JsonArray array => array,
        JsonObject wrapper when wrapper["$values"] is JsonArray array => array,
        _ => throw new InvalidDataException("插件仓库列表格式无效。"),
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
