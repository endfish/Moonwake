using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Models;
using Moonwake.Services;

if (args.Contains("--child")) { await Task.Delay(700); return 0; }
var previousHome = Environment.GetEnvironmentVariable("MOONWAKE_HOME");
var testRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N")));
Environment.SetEnvironmentVariable("MOONWAKE_HOME", testRoot);
var passed = 0;
try
{
    var paths = StandalonePaths.Create();
    paths.EnsureDirectories();
    using var logger = new StandaloneLogger(paths);
    var config = new ConfigurationStore(paths).LoadOrCreate();
    Assert(config.SupportedGameVersions.Count == 0 && !config.LoadPluginsByDefault, "new installs have no admitted versions or enabled plugins");
    Assert(paths.HomeDirectory == testRoot && !paths.HomeDirectory.Contains("XIVLauncher"), "isolated home selection");
    paths.EnsureConfiguredDirectories(config);
    Assert(!new PayloadValidator(paths).Validate(config).IsValid, "empty resource directories are not considered a prepared installation");
    var service = new InjectionService(paths, new PayloadValidator(paths), logger);
    foreach (var state in new[] { DalamudModuleState.Loaded, DalamudModuleState.Unknown, DalamudModuleState.NotLoaded })
    {
        var result = await service.InjectAsync(new GameProcessInfo(int.MaxValue, null, "unverified", state, null), config, false, false, CancellationToken.None);
        Assert(!result.Success, $"{state}: gate refuses before invoking an injector or accessing a PID");
    }
    using var current = Process.GetCurrentProcess();
    var self = new GameProcessInfo(current.Id, null, "test", DalamudModuleState.NotLoaded, null, current.StartTime.ToUniversalTime().Ticks);
    ExpectFailure(() => { using var invalid = GameSession.Capture(self with { StartTimeUtcTicks = self.StartTimeUtcTicks + 1 }); }, "reused PID / changed instance is rejected");
    using (var session = GameSession.Capture(self))
    {
        using var cancellation = new CancellationTokenSource(50);
        try { await session.WaitForExitAsync(cancellation.Token); throw new Exception("Wait must cancel."); }
        catch (OperationCanceledException) { }
        Assert(!current.HasExited, "cancelling the companion does not kill its target");
    }
    var childInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    childInfo.ArgumentList.Add("--child");
    using (var child = Process.Start(childInfo)!)
    {
        var info = new GameProcessInfo(child.Id, null, "test", DalamudModuleState.NotLoaded, null, child.StartTime.ToUniversalTime().Ticks);
        using var session = GameSession.Capture(info);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await session.WaitForExitAsync(timeout.Token);
        Assert(session.HasExited, "the selected process exit completes companion lifetime");
    }
    var payload = Path.Combine(testRoot, "payload");
    Directory.CreateDirectory(payload);
    var files = new Dictionary<string,string>();
    foreach (var name in new[] { "Dalamud.dll", "Dalamud.Boot.dll", "Dalamud.Injector.exe" })
    {
        File.WriteAllText(Path.Combine(payload, name), "integrity test fixture, never executed");
        files[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(payload, name))));
    }
    var manifest = Path.Combine(payload, "moonwake-build.json");
    WriteManifest();
    PayloadIntegrity.Verify(payload);
    Assert(true, "complete payload hashes validate");
    File.AppendAllText(Path.Combine(payload, "Dalamud.dll"), "tamper");
    ExpectFailure(() => PayloadIntegrity.Verify(payload), "modified payload is rejected");
    files["Dalamud.dll"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(payload, "Dalamud.dll"))));
    files["../outside.dll"] = new string('0', 64);
    WriteManifest();
    ExpectFailure(() => PayloadIntegrity.Verify(payload), "manifest path traversal is rejected");
    passed += ImportTests.Run(paths);
    Console.WriteLine($"PASS: {passed} checks");
    return 0;

    void WriteManifest() => File.WriteAllText(manifest, JsonSerializer.Serialize(new { product="Moonwake", schemaVersion=1, files }));
}
finally { Environment.SetEnvironmentVariable("MOONWAKE_HOME", previousHome); }

void Assert(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    passed++;
    Console.WriteLine("PASS: " + description);
}
void ExpectFailure(Action action, string description)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { Assert(true, description); return; }
    throw new Exception("FAIL: " + description);
}
