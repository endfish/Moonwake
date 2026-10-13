using Moonwake.Configuration;
using Moonwake.Models;

namespace Moonwake.Services;

internal sealed record LauncherReadiness(bool CanInject, string Message)
{
    public static LauncherReadiness Evaluate(GameProcessInfo? target, StandaloneConfiguration configuration, bool resourcesReady)
    {
        if (target is null) return Unavailable("启动游戏后，将自动选择进程。");
        if (target.DalamudState == DalamudModuleState.Loaded) return Unavailable("此进程已加载 Dalamud。");
        if (target.DalamudState == DalamudModuleState.Unknown) return Unavailable("无法读取游戏模块，请确认已允许管理员运行。");
        if (target.GameVersion is null) return Unavailable("无法读取游戏版本，请检查游戏文件。");
        if (!resourcesReady) return Unavailable("本地核心与运行资源尚未准备完成。");
        if (!configuration.IsGameVersionSupported(target.GameVersion))
            return Unavailable($"尚未适配游戏版本 {target.GameVersion}，请更新 Moonwake。");
        return new(true, "注入后隐藏窗口，游戏退出时自动关闭。");
    }

    private static LauncherReadiness Unavailable(string message) => new(false, message);
}
