using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moonwake.Infrastructure;
using Moonwake.Services;

internal static class UpdateTests
{
    internal static async Task<int> RunAsync(string root)
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
        void Reject(Action action, string name)
        {
            try { action(); } catch (Exception e) when (e is IOException or InvalidDataException) { Check(true, name); return; }
            throw new Exception(name);
        }
        Check(ReleaseVersion.Parse("v0.2.0-preview.10").CompareTo(ReleaseVersion.Parse("0.2.0-preview.9")) > 0 &&
            ReleaseVersion.Parse("0.2.0").CompareTo(ReleaseVersion.Parse("0.2.0-preview.10")) > 0 &&
            ReleaseVersion.Parse("0.2.0+abc").CompareTo(ReleaseVersion.Parse("0.2.0+def")) == 0, "semantic version ordering handles preview numeric identifiers, stable promotion and build metadata");
        var zip = Package(new() { ["Moonwake.exe"] = "new executable", ["使用说明.txt"] = "new instructions", ["licenses/LICENSE.txt"] = "new license" });
        var digest = Convert.ToHexString(SHA256.HashData(zip));
        object Release(string tag, bool preview = true, bool draft = false, string? url = null, string? hash = null) => new
        {
            tag_name = tag, prerelease = preview, draft, body = "测试更新说明", assets = new[] { new { name = $"Moonwake-{tag.TrimStart('v')}-win-x64.zip",
                browser_download_url = url ?? $"https://github.com/endfish/Moonwake/releases/download/{tag}/Moonwake-{tag.TrimStart('v')}-win-x64.zip", size = zip.Length, digest = hash ?? "sha256:" + digest } },
        };
        var data = JsonSerializer.SerializeToUtf8Bytes(new[] { Release("v0.2.0-preview.10"), Release("v0.2.0-preview.9"), Release("v0.2.0", false), Release("v1.0.0", false, true) });
        Check(UpdateService.SelectRelease(data, "0.2.0-preview.1")?.Tag == "v0.2.0", "preview users receive a later stable release and drafts are ignored");
        Check(UpdateService.SelectRelease(JsonSerializer.SerializeToUtf8Bytes(new[] { Release("v0.3.0-preview.1"), Release("v0.2.1", false) }), "0.2.0")?.Tag == "v0.2.1", "stable users do not receive preview builds");
        Check(UpdateService.SelectRelease(JsonSerializer.SerializeToUtf8Bytes(new[] { Release("v0.2.0-preview.10"), Release("v0.2.0-preview.9") }), "0.2.0-preview.1")?.Tag == "v0.2.0-preview.10", "preview discovery uses release listings and semantic order");
        foreach (var bad in new[] { Release("v0.3.0", url: "https://example.test/Moonwake.zip"), Release("v0.3.0", hash: "sha256:bad"),
            Release("v0.3.0", url: "http://github.com/endfish/Moonwake/releases/download/v0.3.0/Moonwake-0.3.0-win-x64.zip") })
            Check(UpdateService.SelectRelease(JsonSerializer.SerializeToUtf8Bytes(new[] { bad }), "0.2.0-preview.1") is null, "untrusted URL, missing checksum or plain HTTP asset is rejected");
        var paths = StandalonePaths.ForRoot(Path.Combine(root, "updates-profile"));
        paths.EnsureDirectories();
        var handler = new Handler { Response = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) } };
        using var service = new UpdateService(paths, new HttpClient(handler), "0.2.0-preview.1");
        var result = await service.CheckAsync(false, default);
        Check(result.Release?.Tag == "v0.2.0" && handler.Calls == 1, "first startup discovers updates without authentication");
        result = await service.CheckAsync(false, default);
        Check(result.Cached && result.Release?.Tag == "v0.2.0" && handler.Calls == 1, "successful automatic checks are cached for 24 hours");
        handler.Response = _ => new(HttpStatusCode.TooManyRequests);
        result = await service.CheckAsync(true, default);
        Check(result.Error == UpdateService.NetworkMessage && handler.Calls == 2, "manual check bypasses cache and rate limiting becomes a nonfatal network message");
        await service.CheckAsync(false, default);
        Check(handler.Calls == 2, "failed automatic checks back off instead of repeatedly calling GitHub");
        handler.Response = _ => throw new HttpRequestException("offline");
        Check((await service.CheckAsync(true, default)).Error == UpdateService.NetworkMessage, "offline errors leave the installed application usable");
        handler.Response = _ => new(HttpStatusCode.OK) { Content = new StringContent("invalid json") };
        Check((await service.CheckAsync(true, default)).Error is not null, "malformed release metadata is nonfatal");
        handler.Response = _ => new(HttpStatusCode.OK) { Content = new StringContent("[{}]") };
        Check((await service.CheckAsync(true, default)).Error is not null, "incomplete release metadata cannot crash launcher startup");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { await service.CheckAsync(true, canceled.Token); throw new Exception("check should cancel"); }
            catch (OperationCanceledException) { Check(true, "closing the launcher cancels a pending update check"); }
        }
        var release = new AvailableRelease("v0.3.0", "notes", "", "https://github.com/endfish/Moonwake/releases/download/v0.3.0/Moonwake-0.3.0-win-x64.zip", zip.Length, digest);
        handler.Response = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        var staged = await service.DownloadAsync(release, Path.Combine(root, "downloads"), null, default);
        Check(File.Exists(Path.Combine(staged, "Moonwake.exe")) && File.Exists(Path.Combine(staged, "ready.json")), "a complete download is verified and safely staged");
        var target = Path.Combine(root, "update-tool"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Moonwake.exe"), "old executable");
        Directory.CreateDirectory(Path.Combine(target, "licenses"));
        File.WriteAllText(Path.Combine(target, "licenses/LICENSE.txt"), "old license");
        var personal = Path.Combine(paths.PluginConfigurationsDirectory, "Personal.json"); File.WriteAllText(personal, "keep plugin settings");
        UpdateInstaller.Apply(staged, target);
        Check(File.ReadAllText(Path.Combine(target, "Moonwake.exe")) == "new executable" && File.ReadAllText(personal) == "keep plugin settings" &&
            Directory.GetFiles(Path.Combine(target, ".moonwake-previous"), "Moonwake.exe", SearchOption.AllDirectories).Any(p => File.ReadAllText(p) == "old executable"),
            "successful update replaces only tool files and retains the previous executable and player data");
        File.WriteAllText(Path.Combine(target, "Moonwake.exe"), "old executable");
        File.WriteAllText(Path.Combine(target, "licenses/LICENSE.txt"), "old license");
        Reject(() => UpdateInstaller.Apply(staged, target, name => { if (name == "Moonwake.exe") throw new IOException("simulated locked executable"); }), "a replacement failure rolls back all earlier changes");
        Check(File.ReadAllText(Path.Combine(target, "licenses/LICENSE.txt")) == "old license" && File.ReadAllText(Path.Combine(target, "Moonwake.exe")) == "old executable", "rollback restores original tool and license files");
        using (var locked = new FileStream(Path.Combine(target, "Moonwake.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(() => UpdateInstaller.Apply(staged, target), "a real locked executable fails safely on Windows");
        Check(File.ReadAllText(Path.Combine(target, "licenses/LICENSE.txt")) == "old license", "locked executable rollback preserves original licenses");
        File.AppendAllText(Path.Combine(staged, "Moonwake.exe"), "tamper");
        Reject(() => UpdateInstaller.Apply(staged, target), "modified staging files are rejected before touching the installed program");
        handler.Response = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("corrupt")) };
        try { await service.DownloadAsync(release, Path.Combine(root, "bad-downloads"), null, default); throw new Exception("download must fail"); }
        catch (InvalidDataException) { Check(File.ReadAllText(Path.Combine(target, "Moonwake.exe")) == "old executable", "truncated downloads cannot replace an installed executable"); }
        handler.Response = _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(zip.Select(b => (byte)(b ^ 1)).ToArray()) };
        try { await service.DownloadAsync(release, Path.Combine(root, "bad-hash"), null, default); throw new Exception("hash must fail"); }
        catch (InvalidDataException) { Check(true, "same-size corrupted downloads fail SHA256 validation"); }
        foreach (var name in new[] { "../outside.txt", "licenses/../../outside.txt", "pluginConfigs/Personal.json", "licenses\\bad.txt", "licenses/bad.txt:stream" })
        {
            var job = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
            var archive = Path.Combine(job, "test.zip"); File.WriteAllBytes(archive, Package(new() { [name] = "bad" }));
            Reject(() => UpdateInstaller.Stage(archive, job), "archive traversal, alternate streams and player-data paths are rejected");
        }
        return count;
    }

    private static byte[] Package(Dictionary<string, string> files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var file in files) { using var writer = new StreamWriter(zip.CreateEntry("Moonwake/" + file.Key).Open()); writer.Write(file.Value); }
        return buffer.ToArray();
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal Func<HttpRequestMessage, HttpResponseMessage> Response = _ => new(HttpStatusCode.NotFound);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Calls++;
            if (request.Headers.Authorization is not null) throw new Exception("updater must not send credentials");
            return Task.FromResult(this.Response(request));
        }
    }
}
