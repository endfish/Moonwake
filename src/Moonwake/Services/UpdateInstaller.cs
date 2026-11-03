using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Moonwake.Services;

internal sealed record UpdateTicket(int ParentId, long ParentStart, string Target, string Stage, string OriginalHash, bool Relaunch = true);

internal static class UpdateInstaller
{
    internal static string Stage(string archive, string job)
    {
        var stage = Path.Combine(job, "ready");
        Directory.CreateDirectory(stage);
        using var zip = ZipFile.OpenRead(archive);
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
        if (entries.Length is 0 or > 500) throw new InvalidDataException("更新包文件数量无效。");
        var prefix = entries.All(e => e.FullName.StartsWith("Moonwake/", StringComparison.Ordinal)) ? "Moonwake/" : "";
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in entries)
        {
            var name = entry.FullName[prefix.Length..];
            expanded += entry.Length;
            if (expanded > 2L * 1024 * 1024 * 1024 || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 ||
                !Allowed(name) || hashes.ContainsKey(name)) throw new InvalidDataException("更新包包含不允许的文件。");
            var destination = SafePath(stage, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var input = entry.Open())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
            hashes.Add(name, Hash(destination));
        }
        if (!hashes.ContainsKey("Moonwake.exe") || !hashes.ContainsKey("使用说明.txt") || !hashes.Keys.Any(k => k.StartsWith("licenses/", StringComparison.Ordinal)))
            throw new InvalidDataException("更新包缺少程序或许可说明。");
        File.WriteAllText(Path.Combine(stage, "ready.json"), JsonSerializer.Serialize(hashes));
        return stage;
    }

    internal static bool Allowed(string name) => name is "Moonwake.exe" or "使用说明.txt" ||
        (name.StartsWith("licenses/", StringComparison.Ordinal) && name.Length > 9 &&
            Path.GetExtension(name).ToLowerInvariant() is ".txt" or ".md" or "");

    internal static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("更新文件路径无效。");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新文件超出工具目录。");
        for (var item = path; item is not null; item = Path.GetDirectoryName(item))
        {
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("自动更新不支持符号链接或目录联接，请手动下载更新。");
        }
        return path;
    }

    internal static void Begin(string stage)
    {
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位 Moonwake 程序。");
        if (!Path.GetFileName(target).Equals("Moonwake.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请将程序命名为 Moonwake.exe，或打开发布页手动更新。");
        var job = Path.GetDirectoryName(stage)!;
        var helper = Path.Combine(job, "Moonwake.Update.exe");
        File.Copy(target, helper, false);
        using var parent = Process.GetCurrentProcess();
        var ticket = new UpdateTicket(parent.Id, parent.StartTime.ToUniversalTime().Ticks, target, stage, Hash(target));
        var ticketPath = Path.Combine(job, "update.json");
        File.WriteAllText(ticketPath, JsonSerializer.Serialize(ticket));
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = job };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(ticketPath);
        using var worker = Process.Start(start) ?? throw new IOException("无法启动更新程序，请手动下载更新。");
    }

    internal static int RunWorker(string ticketPath)
    {
        string? target = null;
        try
        {
            var ticket = JsonSerializer.Deserialize<UpdateTicket>(File.ReadAllText(ticketPath)) ?? throw new InvalidDataException("更新任务为空。");
            target = Path.GetFullPath(ticket.Target);
            var job = Path.GetDirectoryName(Path.GetFullPath(ticketPath))!;
            if (!Path.GetFileName(target).Equals("Moonwake.exe", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(ticket.Stage) != Path.Combine(job, "ready") ||
                !string.Equals(Path.GetDirectoryName(Environment.ProcessPath), job, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新任务路径无效。");
            try
            {
                using var parent = Process.GetProcessById(ticket.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks != ticket.ParentStart) throw new InvalidOperationException("原程序实例已经变化。");
                if (!string.Equals(parent.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("原程序路径已经变化。");
                if (!parent.WaitForExit(30000)) throw new IOException("Moonwake 尚未退出，请关闭后手动更新。");
            }
            catch (ArgumentException) { /* The parent has already exited. */ }
            if (!Hash(target).Equals(ticket.OriginalHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("程序已被其他更新修改，请重新启动后检查更新。");
            using var updateLock = new FileStream(Path.Combine(Path.GetDirectoryName(target)!, ".moonwake-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Apply(ticket.Stage, Path.GetDirectoryName(target)!);
            if (ticket.Relaunch) Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target)! })?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("更新未完成，原程序和插件数据已保留。\n" + ex.Message + "\n可以从 GitHub 发布页手动下载。", "Moonwake 更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (target is not null && File.Exists(target))
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { }
            return 1;
        }
    }

    internal static void Apply(string stage, string targetRoot, Action<string>? beforeReplace = null)
    {
        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(stage, "ready.json")))
            ?? throw new InvalidDataException("更新文件清单为空。");
        if (hashes.Count is 0 or > 500 || !hashes.ContainsKey("Moonwake.exe")) throw new InvalidDataException("更新文件清单无效。");
        foreach (var item in hashes)
            if (!Allowed(item.Key) || !Hash(SafePath(stage, item.Key)).Equals(item.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("待安装更新校验失败。");
        var transaction = ".moonwake-update-" + Guid.NewGuid().ToString("N");
        var localStage = SafePath(targetRoot, transaction);
        var backup = SafePath(targetRoot, ".moonwake-previous/" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(localStage);
        Directory.CreateDirectory(backup);
        var changed = new List<(string Destination, string Backup, bool HadOriginal)>();
        try
        {
            foreach (var item in hashes)
            {
                var local = SafePath(localStage, item.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                File.Copy(SafePath(stage, item.Key), local);
                if (Hash(local) != item.Value.ToUpperInvariant()) throw new InvalidDataException("工具目录中的更新副本校验失败。");
            }
            foreach (var name in hashes.Keys.OrderBy(n => n == "Moonwake.exe" ? 1 : 0))
            {
                var destination = SafePath(targetRoot, name);
                var saved = SafePath(backup, name);
                var local = SafePath(localStage, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                beforeReplace?.Invoke(name);
                var existed = File.Exists(destination);
                if (existed) File.Replace(local, destination, saved);
                else File.Move(local, destination);
                changed.Add((destination, saved, existed));
            }
        }
        catch
        {
            foreach (var item in changed.AsEnumerable().Reverse())
            {
                if (item.HadOriginal) File.Move(item.Backup, item.Destination, true);
                else File.Delete(item.Destination);
            }
            throw;
        }
    }

    internal static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
