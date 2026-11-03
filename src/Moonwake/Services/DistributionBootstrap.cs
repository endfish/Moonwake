using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Moonwake.Configuration;
using Moonwake.Infrastructure;

namespace Moonwake.Services;

internal sealed record DistributionManifest(int SchemaVersion, string ReleaseId, string PayloadDirectory,
    string AssetDirectory, string RuntimeDirectory, string[] SupportedGameVersions, Dictionary<string, string> Files);

internal static class DistributionBootstrap
{
    private static readonly Assembly Assembly = typeof(DistributionBootstrap).Assembly;
    public static bool IsBundled => Assembly.GetManifestResourceInfo("Moonwake.Distribution.json") is not null;

    public static StandaloneConfiguration Prepare(StandalonePaths paths, Action<string>? progress = null)
    {
        if (!IsBundled) return new ConfigurationStore(paths).LoadOrCreate();
        using var manifest = Assembly.GetManifestResourceStream("Moonwake.Distribution.json")!;
        using var archive = Assembly.GetManifestResourceStream("Moonwake.Distribution.zip")!;
        return Prepare(paths, manifest, archive, progress);
    }

    internal static StandaloneConfiguration Prepare(StandalonePaths paths, Stream manifestStream, Stream archiveStream, Action<string>? progress = null)
    {
        paths.EnsureDirectories();
        using var installationLock = new FileStream(Path.Combine(paths.HomeDirectory, ".bootstrap.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var manifest = JsonSerializer.Deserialize<DistributionManifest>(manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("发行资源清单为空。");
        if (manifest.SchemaVersion != 1 || manifest.Files.Count == 0 || manifest.SupportedGameVersions.Length == 0)
            throw new InvalidDataException("发行资源清单无效。");
        var components = new[] { manifest.PayloadDirectory, manifest.AssetDirectory, manifest.RuntimeDirectory };
        var expectedRoots = new[] { "payloads/", "assets/", "runtime/" };
        for (var i = 0; i < components.Length; i++)
        {
            if (!components[i].StartsWith(expectedRoots[i], StringComparison.Ordinal) || components[i].EndsWith('/'))
                throw new InvalidDataException("发行资源目录无效。");
            SafePath(paths.HomeDirectory, components[i]);
        }
        foreach (var file in manifest.Files)
        {
            SafePath(paths.HomeDirectory, file.Key);
            if (!components.Any(root => file.Key.StartsWith(root + "/", StringComparison.Ordinal)) ||
                file.Value.Length != 64 || !file.Value.All(Uri.IsHexDigit)) throw new InvalidDataException("发行资源清单含不允许的文件。");
        }
        var store = new ConfigurationStore(paths);
        var config = store.LoadOrCreate();
        var current = new[] { config.PayloadDirectory, config.AssetDirectory, config.RuntimeDirectory };
        var selected = new string[3];
        var required = new HashSet<int>();
        for (var i = 0; i < components.Length; i++)
        {
            progress?.Invoke("正在检查运行资源…");
            var candidates = new[] { current[i], components[i] }.Distinct(StringComparer.OrdinalIgnoreCase);
            var match = candidates.FirstOrDefault(candidate => Matches(paths.HomeDirectory, candidate, components[i], manifest.Files));
            if (match is not null) { selected[i] = match.Replace('\\', '/'); continue; }
            selected[i] = components[i];
            if (Directory.Exists(SafePath(paths.HomeDirectory, selected[i]))) selected[i] += "-repair-" + Guid.NewGuid().ToString("N")[..8];
            required.Add(i);
        }
        if (required.Count > 0)
        {
            var stage = Path.Combine(paths.TempDirectory, "prepare-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, true);
            var encountered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                SafePath(stage, entry.FullName);
                if (!encountered.Add(entry.FullName) || !manifest.Files.TryGetValue(entry.FullName, out var hash))
                    throw new InvalidDataException("发行包文件与清单不匹配。");
                var component = Array.FindIndex(components, root => entry.FullName.StartsWith(root + "/", StringComparison.Ordinal));
                if (!required.Contains(component)) continue;
                progress?.Invoke("正在准备运行资源… " + Path.GetFileName(entry.FullName));
                var destination = SafePath(stage, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var input = entry.Open())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                if (Hash(destination) != hash) throw new InvalidDataException("发行资源校验失败：" + entry.FullName);
            }
            if (encountered.Count != manifest.Files.Count) throw new InvalidDataException("发行包缺少资源文件。");
            foreach (var index in required)
            {
                var destination = SafePath(paths.HomeDirectory, selected[index]);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                FileOperationRetry.MoveDirectory(SafePath(stage, components[index]), destination);
            }
            // Only remove our unique staging directory; never remove an existing component or user directory.
            if (!Path.GetFullPath(stage).StartsWith(Path.GetFullPath(paths.TempDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("临时目录无效。");
            try { Directory.Delete(stage, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Verified components are ready; temporary cleanup can wait. */ }
        }
        if (config.PayloadDirectory != selected[0] || config.AssetDirectory != selected[1] || config.RuntimeDirectory != selected[2] ||
            !config.SupportedGameVersions.SequenceEqual(manifest.SupportedGameVersions))
        {
            config.PayloadDirectory = selected[0];
            config.AssetDirectory = selected[1];
            config.RuntimeDirectory = selected[2];
            config.SupportedGameVersions = [.. manifest.SupportedGameVersions];
            store.Save(config);
        }
        return config;
    }

    private static bool Matches(string home, string candidate, string component, Dictionary<string, string> files)
    {
        if (Path.IsPathFullyQualified(candidate)) return false;
        string root;
        try { root = SafePath(home, candidate.Replace('\\', '/')); }
        catch (InvalidDataException) { return false; }
        var expected = files.Where(item => item.Key.StartsWith(component + "/", StringComparison.Ordinal)).ToArray();
        return expected.Length > 0 && Directory.Exists(root) && expected.All(item =>
        {
            var file = SafePath(root, item.Key[(component.Length + 1)..]);
            return File.Exists(file) && Hash(file) == item.Value;
        });
    }

    private static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidDataException("资源路径无效。");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("资源路径超出目标目录。");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("运行资源不允许使用符号链接或目录联接。");
            if (string.Equals(current, Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
