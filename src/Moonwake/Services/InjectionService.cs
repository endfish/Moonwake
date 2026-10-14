using System.Diagnostics;
using System.Globalization;

using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Models;

namespace Moonwake.Services;

internal sealed class InjectionService(
    StandalonePaths paths,
    PayloadValidator payloadValidator,
    StandaloneLogger logger)
{
    public async Task<InjectionResult> InjectAsync(
        GameProcessInfo target,
        StandaloneConfiguration configuration,
        bool researchMode,
        bool noPlugins,
        CancellationToken cancellationToken)
    {
        if (target.DalamudState == DalamudModuleState.Loaded)
            return InjectionResult.Refused($"PID {target.ProcessId} 已加载 Dalamud.dll，拒绝重复注入。");

        if (target.DalamudState == DalamudModuleState.Unknown)
            return InjectionResult.Refused($"无法确认 PID {target.ProcessId} 的模块状态，拒绝注入。{target.InspectionError}");

        var isSupported = configuration.IsGameVersionSupported(target.GameVersion);
        if (!isSupported && !researchMode)
        {
            var version = target.GameVersion ?? "未知";
            return InjectionResult.Refused($"游戏版本 {version} 尚未列入 supportedGameVersions；普通模式拒绝注入。");
        }

        if (researchMode)
        {
            noPlugins = true;
            logger.Warn($"研究模式：PID {target.ProcessId}，游戏版本 {target.GameVersion ?? "未知"}，强制禁用全部插件。");
        }

        var payload = payloadValidator.Validate(configuration);
        if (!payload.IsValid)
            return InjectionResult.Refused(string.Join(Environment.NewLine, payload.Errors));

        PayloadIntegrity.Verify(payload.Layout.PayloadDirectory);

        // Capture the instance before passing its PID to the injector; retain the handle until it completes.
        using var targetSession = GameSession.Capture(target);

        var startInfo = this.CreateStartInfo(target, configuration, payload.Layout, researchMode, noPlugins);

        logger.Info($"调用 Injector 注入 PID {target.ProcessId}。Payload={payload.Layout.PayloadDirectory}; Configuration={paths.DalamudConfigurationPath}; Plugins={paths.InstalledPluginsDirectory}");

        using var injector = new Process { StartInfo = startInfo };
        if (!injector.Start())
            return InjectionResult.Refused("无法启动 Dalamud.Injector.exe。");

        var standardOutputTask = injector.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = injector.StandardError.ReadToEndAsync(cancellationToken);
        await injector.WaitForExitAsync(cancellationToken);
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        foreach (var line in SplitLines(standardOutput))
            logger.Info($"Injector: {line}");
        foreach (var line in SplitLines(standardError))
            logger.Warn($"Injector: {line}");

        return injector.ExitCode == 0
                   ? new InjectionResult(true, 0, $"Injector 已完成 PID {target.ProcessId} 的注入调用。")
                   : new InjectionResult(false, injector.ExitCode, $"Injector 退出码：{injector.ExitCode}");
    }

    internal ProcessStartInfo CreateStartInfo(GameProcessInfo target, StandaloneConfiguration configuration,
        PayloadLayout payload, bool researchMode, bool noPlugins)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = payload.InjectorPath,
            WorkingDirectory = payload.PayloadDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.Environment.Remove("DALAMUD_NOT_HAVE_PLUGINS");
        startInfo.Environment["DOTNET_ROOT"] = payload.RuntimeDirectory;
        startInfo.Environment["DOTNET_ROOT_X64"] = payload.RuntimeDirectory;
        startInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";

        AddArgument(startInfo, "inject");
        AddArgument(startInfo, target.ProcessId.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, $"--dalamud-working-directory={payload.PayloadDirectory}");
        AddArgument(startInfo, $"--dalamud-configuration-path={paths.DalamudConfigurationPath}");
        AddArgument(startInfo, $"--dalamud-plugin-directory={paths.InstalledPluginsDirectory}");
        AddArgument(startInfo, $"--dalamud-asset-directory={payload.AssetDirectory}");
        AddArgument(startInfo, $"--dalamud-runtime-directory={payload.RuntimeDirectory}");
        AddArgument(startInfo, $"--dalamud-temp-directory={paths.TempDirectory}");
        AddArgument(startInfo, $"--dalamud-delay-initialize={configuration.InitializationDelayMs}");
        AddArgument(startInfo, "--dalamud-client-language=chinesesimplified");
        AddArgument(startInfo, $"--logpath={paths.LogDirectory}");
        AddArgument(startInfo, "--logname=moonwake");
        AddArgument(startInfo, "-v");

        if (researchMode)
            AddArgument(startInfo, "--warn");

        if (researchMode || noPlugins || !configuration.LoadPluginsByDefault)
            AddArgument(startInfo, "--no-plugin");

        return startInfo;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string argument) =>
        startInfo.ArgumentList.Add(argument);

    private static IEnumerable<string> SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
