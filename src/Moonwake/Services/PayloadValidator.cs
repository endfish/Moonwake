using Moonwake.Configuration;
using Moonwake.Infrastructure;

namespace Moonwake.Services;

internal sealed class PayloadValidator(StandalonePaths paths)
{
    public PayloadValidationResult Validate(StandaloneConfiguration configuration)
    {
        var layout = paths.ResolvePayloadLayout(configuration);
        var errors = new List<string>();

        RequireDirectory(layout.PayloadDirectory, "Payload 目录", errors);
        RequireFile(layout.InjectorPath, "Dalamud.Injector.exe", errors);
        RequireFile(layout.BootPath, "Dalamud.Boot.dll", errors);
        RequireFile(layout.DalamudPath, "Dalamud.dll", errors);
        RequireDirectory(layout.AssetDirectory, "Assets 目录", errors);
        RequireFile(Path.Combine(layout.PayloadDirectory, "moonwake-build.json"), "Moonwake 核心构建清单", errors);
        RequireDirectory(layout.RuntimeDirectory, "Runtime 目录", errors);
        foreach (var asset in new[] { "NotoSansCJKsc-Medium.otf", "NotoSansCJK-Regular.ttc", "NotoSansCJK-Medium.ttc", "FontAwesome710FreeSolid.otf", "Inconsolata-Regular.ttf", "logo.png" })
            RequireFile(Path.Combine(layout.AssetDirectory, "UIRes", asset), asset, errors);
        RequireRuntime(layout.RuntimeDirectory, "host/fxr", "hostfxr.dll", errors);
        RequireRuntime(layout.RuntimeDirectory, "shared/Microsoft.NETCore.App", "coreclr.dll", errors);
        RequireRuntime(layout.RuntimeDirectory, "shared/Microsoft.WindowsDesktop.App", "PresentationFramework.dll", errors);

        return new PayloadValidationResult(layout, errors);
    }

    private static void RequireDirectory(string path, string description, ICollection<string> errors)
    {
        if (!Directory.Exists(path))
            errors.Add($"缺少{description}：{path}");
    }

    private static void RequireRuntime(string root, string component, string file, ICollection<string> errors)
    {
        var path = Path.Combine(root, component);
        if (!Directory.Exists(path) || !Directory.EnumerateDirectories(path, "10.0.*")
                .Any(directory => Version.TryParse(Path.GetFileName(directory), out _) && File.Exists(Path.Combine(directory, file))))
            errors.Add($"缺少 .NET 10 运行组件：{component}/{file}");
    }

    private static void RequireFile(string path, string description, ICollection<string> errors)
    {
        if (!File.Exists(path))
            errors.Add($"缺少 {description}：{path}");
    }
}

internal sealed record PayloadValidationResult(PayloadLayout Layout, IReadOnlyList<string> Errors)
{
    public bool IsValid => this.Errors.Count == 0;
}
