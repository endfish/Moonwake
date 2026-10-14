using System.Diagnostics;
using System.Text.Json;
using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Models;
using Moonwake.Services;

internal static class StartupTests
{
    public static int Run(string root)
    {
        var passed = 0;
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN");
        var actualPaths = StandalonePaths.Create();
        Check(actualPaths.HomeDirectory == expected &&
              actualPaths.DalamudConfigurationPath == Path.Combine(expected, "moonwake", "config.json") &&
              actualPaths.CnConfigurationPath == Path.Combine(expected, "dalamudConfig.json") &&
              actualPaths.PluginConfigurationsDirectory == Path.Combine(expected, "pluginConfigs") &&
              actualPaths.InstalledPluginsDirectory == Path.Combine(expected, "installedPlugins"),
              "desktop isolates core settings while sharing installed plugins and plugin configuration");
        var configuration = new StandaloneConfiguration();
        var target = new GameProcessInfo(12345, "test.exe", "2026.10.06.0000.0000", DalamudModuleState.NotLoaded, null, 1);
        var normal = LauncherReadiness.Evaluate(target, configuration, true);
        Check(normal.CanInject && configuration.LoadPluginsByDefault, "adapted client uses ordinary injection with plugin loading enabled");
        Check(!LauncherReadiness.Evaluate(target with { GameVersion = "future-client" }, configuration, true).CanInject, "unadapted future client is not silently injected");
        Check(!LauncherReadiness.Evaluate(target, configuration, false).CanInject, "ordinary injection requires prepared resources");
        Check(!LauncherReadiness.Evaluate(target with { GameVersion = null }, configuration, true).CanInject, "unreadable game version is not treated as supported");
        Check(!LauncherReadiness.Evaluate(target with { DalamudState = DalamudModuleState.Unknown }, configuration, true).CanInject, "ordinary injection does not bypass unreadable module state");
        Check(!LauncherReadiness.Evaluate(target with { DalamudState = DalamudModuleState.Loaded }, configuration, true).CanInject, "ordinary injection refuses an already injected process");
        var paths = StandalonePaths.ForRoot(root);
        using var logger = new StandaloneLogger(paths);
        var service = new InjectionService(paths, new PayloadValidator(paths), logger);
        var payload = paths.ResolvePayloadLayout(configuration);
        var normalStart = service.CreateStartInfo(target, configuration, payload, false, false);
        Check(normalStart.ArgumentList.Contains($"--dalamud-configuration-path={paths.DalamudConfigurationPath}") &&
              normalStart.ArgumentList.Contains($"--dalamud-plugin-directory={paths.InstalledPluginsDirectory}"), "configuration and plugin paths are explicitly passed to the injector");
        Check(!normalStart.ArgumentList.Contains("--no-plugin") && !normalStart.ArgumentList.Contains("--warn") &&
              !normalStart.Environment.ContainsKey("DALAMUD_NOT_HAVE_PLUGINS"), "normal desktop injection sends no safe-mode flags or inherited no-plugin override");
        Check(service.CreateStartInfo(target, configuration, payload, false, true).ArgumentList.Contains("--no-plugin"), "explicit diagnostic no-plugin option remains available");
        var researchStart = service.CreateStartInfo(target, configuration, payload, true, false);
        Check(researchStart.ArgumentList.Contains("--no-plugin") && researchStart.ArgumentList.Contains("--warn"), "research mode is only used when explicitly requested by the diagnostic caller");
        if (Environment.GetEnvironmentVariable("MOONWAKE_TEST_INJECTOR") is { Length: > 0 } injector)
        {
            normalStart.FileName = injector;
            normalStart.WorkingDirectory = Path.GetDirectoryName(injector)!;
            normalStart.Environment.Remove("DOTNET_ROOT");
            normalStart.Environment.Remove("DOTNET_ROOT_X64");
            normalStart.Environment["__COMPAT_LAYER"] = "RunAsInvoker";
            normalStart.ArgumentList.Add("--print-start-info");
            using var child = Process.Start(normalStart)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var errors = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(15000)) throw new Exception("Injector diagnostics timed out.");
            var json = output.GetAwaiter().GetResult().Split('\n').First(line => line.StartsWith("{\"LoadMethod\""));
            using var parsed = JsonDocument.Parse(json);
            Check(child.ExitCode == 0 && parsed.RootElement.GetProperty("ConfigurationPath").GetString() == paths.DalamudConfigurationPath &&
                  parsed.RootElement.GetProperty("PluginDirectory").GetString() == paths.InstalledPluginsDirectory,
                  "built injector parses the exact host configuration/plugin paths without injecting a process");
        }
        return passed;

        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            passed++;
            Console.WriteLine("PASS: " + description);
        }
    }
}
