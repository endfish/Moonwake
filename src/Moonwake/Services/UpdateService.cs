using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Moonwake.Infrastructure;

namespace Moonwake.Services;

internal sealed record AvailableRelease(string Tag, string Notes, string PageUrl, string DownloadUrl, long Size, string Sha256);
internal sealed record UpdateCheck(AvailableRelease? Release, string? Error = null, bool Cached = false);
internal sealed record UpdateState(DateTimeOffset LastAttempt, DateTimeOffset LastSuccess, AvailableRelease? Release);

internal sealed class UpdateService : IDisposable
{
    internal const string Repository = "endfish/Moonwake";
    internal const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    internal const string NetworkMessage = "无法连接 GitHub。请检查网络连接，必要时使用网络代理后重试。";
    internal static string CurrentVersion => typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    private readonly HttpClient client;
    private readonly string statePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string current;

    public UpdateService(StandalonePaths paths, HttpClient? client = null, string? current = null)
    {
        this.client = client ?? new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = TimeSpan.FromMinutes(30) };
        this.client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Moonwake", "0.2.0"));
        this.statePath = Path.Combine(paths.CoreConfigurationDirectory, "update-state.json");
        this.current = current ?? CurrentVersion;
    }

    public async Task<UpdateCheck> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            UpdateState? state = null;
            try { if (File.Exists(this.statePath)) state = JsonSerializer.Deserialize<UpdateState>(await File.ReadAllTextAsync(this.statePath, cancellationToken)); }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
            if (!force && state is not null && now >= state.LastAttempt &&
                (now - state.LastSuccess < TimeSpan.FromHours(24) || now - state.LastAttempt < TimeSpan.FromHours(1)))
                return new(this.IsNewer(state.Release) ? state.Release : null, Cached: true);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + "/releases?per_page=30");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var response = await this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                var bytes = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(timeout.Token), 2 * 1024 * 1024, timeout.Token);
                var release = SelectRelease(bytes, this.current);
                this.Save(new(now, now, release));
                return new(release);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                this.Save(new(now, state?.LastSuccess ?? default, state?.Release));
                return new(null, NetworkMessage);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or JsonException or FormatException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
            {
                this.Save(new(now, state?.LastSuccess ?? default, state?.Release));
                return new(null, NetworkMessage);
            }
        }
        finally { this.gate.Release(); }
    }

    internal static AvailableRelease? SelectRelease(byte[] json, string installed)
    {
        var current = ReleaseVersion.Parse(installed);
        using var document = JsonDocument.Parse(json);
        var options = new List<AvailableRelease>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.GetProperty("draft").GetBoolean()) continue;
            var tag = entry.GetProperty("tag_name").GetString()!;
            ReleaseVersion version;
            try { version = ReleaseVersion.Parse(tag); } catch (FormatException) { continue; }
            if (version.CompareTo(current) <= 0 || (current.PreRelease.Length == 0 && (entry.GetProperty("prerelease").GetBoolean() || version.PreRelease.Length != 0))) continue;
            var assetName = $"Moonwake-{tag.TrimStart('v')}-win-x64.zip";
            foreach (var asset in entry.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != assetName) continue;
                var url = asset.GetProperty("browser_download_url").GetString()!;
                var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
                var size = asset.GetProperty("size").GetInt64();
                if (!ValidDownload(url, tag, assetName) || digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                    digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit) || size <= 0 || size > 1024L * 1024 * 1024) continue;
                options.Add(new(tag, entry.GetProperty("body").GetString() ?? "", "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag), url, size, digest[7..]));
            }
        }
        return options.OrderByDescending(r => ReleaseVersion.Parse(r.Tag)).FirstOrDefault();
    }

    internal static bool ValidDownload(string url, string tag, string assetName) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com" &&
        uri.UserInfo.Length == 0 && uri.IsDefaultPort && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        Uri.UnescapeDataString(uri.AbsolutePath) == "/" + Repository + "/releases/download/" + tag + "/" + assetName;

    private bool IsNewer(AvailableRelease? release)
    {
        try { return release is not null && !string.IsNullOrWhiteSpace(release.Tag) && !string.IsNullOrWhiteSpace(release.DownloadUrl) &&
            !string.IsNullOrWhiteSpace(release.Sha256) && release.Size is > 0 and <= 1073741824 && ReleaseVersion.Parse(release.Tag).CompareTo(ReleaseVersion.Parse(this.current)) > 0 &&
            ValidDownload(release.DownloadUrl, release.Tag, $"Moonwake-{release.Tag.TrimStart('v')}-win-x64.zip") &&
            release.Sha256.Length == 64 && release.Sha256.All(Uri.IsHexDigit); }
        catch (FormatException) { return false; }
    }

    public async Task<string> DownloadAsync(AvailableRelease release, string updateRoot, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (!this.IsNewer(release)) throw new InvalidDataException("更新信息无效，请重新检查更新。");
        var job = Path.Combine(updateRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        var partial = Path.Combine(job, "package.zip.partial");
        using var transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        transfer.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await this.client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, transfer.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new InvalidDataException("下载大小与发行信息不一致。");
        await using (var input = await response.Content.ReadAsStreamAsync(transfer.Token))
        await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while (true)
            {
                transfer.CancelAfter(TimeSpan.FromSeconds(60));
                read = await input.ReadAsync(buffer, transfer.Token);
                if (read == 0) break;
                total += read;
                if (total > release.Size) throw new InvalidDataException("下载文件超过预期大小。");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                progress?.Report((int)(total * 100 / release.Size));
            }
            if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载文件校验失败，请重试或从 GitHub 发布页重新下载。");
        }
        var verified = Path.Combine(job, "package.zip");
        File.Move(partial, verified);
        var stage = await Task.Run(() => UpdateInstaller.Stage(verified, job), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return stage;
    }

    private void Save(UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.statePath)!);
            var temporary = this.statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, this.statePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken token)
    {
        await using (stream)
        using (var output = new MemoryStream())
        {
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + read > limit) throw new InvalidDataException("发行信息过大。");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
    }

    public void Dispose() => this.client.Dispose();
}
