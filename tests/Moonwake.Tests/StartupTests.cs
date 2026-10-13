using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Models;
using Moonwake.Services;

internal static class StartupTests
{
    public static int Run(string root)
    {
        var passed = 0;
        var executableDirectory = Path.Combine(root, "portable", "shell");
        var roaming = Path.Combine(root, "roaming");
        Directory.CreateDirectory(executableDirectory);
        Check(StandalonePaths.ResolveHome(executableDirectory, roaming, null) == Path.Combine(roaming, "Moonwake"), "unconfigured desktop uses its default isolated profile");
        var selector = Path.Combine(executableDirectory, "moonwake.profile.json");
        File.WriteAllText(selector, "{\"homeDirectory\":\"../profile\"}");
        var expectedHome = Path.GetFullPath(Path.Combine(executableDirectory, "../profile"));
        Check(StandalonePaths.ResolveHome(executableDirectory, roaming, null) == expectedHome, "portable profile is resolved against the EXE directory, independent of shell working directory");
        var environmentHome = Path.Combine(root, "explicit-home");
        Check(StandalonePaths.ResolveHome(executableDirectory, roaming, environmentHome) == environmentHome, "explicit environment profile takes priority over portable selection");
        File.WriteAllText(selector, "{}");
        try { StandalonePaths.ResolveHome(executableDirectory, roaming, null); throw new Exception("Invalid selector accepted"); }
        catch (InvalidDataException) { Check(true, "invalid profile selector does not silently switch to an empty profile"); }

        var configuration = new StandaloneConfiguration();
        var target = new GameProcessInfo(12345, "test.exe", "2026.10.06.0000.0000", DalamudModuleState.NotLoaded, null, 1);
        var normal = LauncherReadiness.Evaluate(target, configuration, true);
        Check(normal.CanInject && configuration.LoadPluginsByDefault, "adapted client uses ordinary injection with plugin loading enabled");
        Check(!LauncherReadiness.Evaluate(target with { GameVersion = "future-client" }, configuration, true).CanInject, "unadapted future client is not silently injected");
        Check(!LauncherReadiness.Evaluate(target, configuration, false).CanInject, "ordinary injection requires prepared resources");
        Check(!LauncherReadiness.Evaluate(target with { GameVersion = null }, configuration, true).CanInject, "unreadable game version is not treated as supported");
        Check(!LauncherReadiness.Evaluate(target with { DalamudState = DalamudModuleState.Unknown }, configuration, true).CanInject, "ordinary injection does not bypass unreadable module state");
        Check(!LauncherReadiness.Evaluate(target with { DalamudState = DalamudModuleState.Loaded }, configuration, true).CanInject, "ordinary injection refuses an already injected process");
        var paths = StandalonePaths.Create();
        using var logger = new StandaloneLogger(paths);
        var service = new InjectionService(paths, new PayloadValidator(paths), logger);
        var payload = paths.ResolvePayloadLayout(configuration);
        var normalStart = service.CreateStartInfo(target, configuration, payload, false, false);
        Check(!normalStart.ArgumentList.Contains("--no-plugin") && !normalStart.ArgumentList.Contains("--warn") &&
              !normalStart.Environment.ContainsKey("DALAMUD_NOT_HAVE_PLUGINS"), "normal desktop injection sends no safe-mode flags or inherited no-plugin override");
        Check(service.CreateStartInfo(target, configuration, payload, false, true).ArgumentList.Contains("--no-plugin"), "explicit diagnostic no-plugin option remains available");
        var researchStart = service.CreateStartInfo(target, configuration, payload, true, false);
        Check(researchStart.ArgumentList.Contains("--no-plugin") && researchStart.ArgumentList.Contains("--warn"), "research mode is only used when explicitly requested by the diagnostic caller");
        return passed;

        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
            passed++;
            Console.WriteLine("PASS: " + description);
        }
    }
}
