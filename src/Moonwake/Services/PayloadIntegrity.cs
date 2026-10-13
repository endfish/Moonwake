using System.Security.Cryptography;
using System.Text.Json;

namespace Moonwake.Services;

internal static class PayloadIntegrity
{
    public static void Verify(string directory)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "moonwake-build.json")));
        var root = json.RootElement;
        if (root.GetProperty("product").GetString() != "Moonwake" || root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("这不是受支持的 Moonwake 核心构建清单。");
        var files = root.GetProperty("files");
        foreach (var required in new[] { "Dalamud.dll", "Dalamud.Boot.dll", "Dalamud.Injector.exe" })
            if (!files.TryGetProperty(required, out _)) throw new InvalidDataException($"构建清单缺少 {required}。");
        var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in files.EnumerateObject())
        {
            var path = Path.GetFullPath(Path.Combine(prefix, entry.Name));
            if (Path.IsPathFullyQualified(entry.Name) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("构建清单包含越界路径。");
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!hash.Equals(entry.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"核心文件校验失败：{entry.Name}。请重新构建或恢复完整载荷。");
        }
    }
}
