using System.Diagnostics;
using Moonwake.Services;

namespace Moonwake.Windows;

internal sealed class UpdateForm : Form
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Label status = new();
    private readonly ProgressBar progress = new();
    private readonly Button download = new();
    private bool downloading;
    private bool closeRequested;
    internal string? ReadyDirectory { get; private set; }

    internal UpdateForm(AvailableRelease release, UpdateService service, string updateRoot)
    {
        this.Text = "Moonwake 更新";
        this.ClientSize = new Size(380, 330);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = this.MinimizeBox = false;
        this.BackColor = Color.FromArgb(18, 26, 33);
        this.ForeColor = Color.FromArgb(232, 238, 244);
        this.Font = new Font("Microsoft YaHei UI", 9F);
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.Controls.Add(new Label { Text = $"发现新版本 {release.Tag}\n当前 {UpdateService.CurrentVersion} · 下载约 {release.Size / 1048576.0:F0} MB", Bounds = new(18, 15, 344, 48) });
        this.Controls.Add(new TextBox { Text = release.Notes.Length > 6000 ? release.Notes[..6000] : release.Notes, Bounds = new(18, 68, 344, 145), ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, BackColor = this.BackColor, ForeColor = this.ForeColor, BorderStyle = BorderStyle.FixedSingle });
        this.status.SetBounds(18, 222, 344, 37);
        this.status.Text = "GitHub 下载较慢时，可尝试切换网络或使用代理。";
        this.Controls.Add(this.status);
        this.progress.SetBounds(18, 263, 344, 8);
        this.Controls.Add(this.progress);
        this.download.Text = "下载并更新";
        this.download.SetBounds(18, 286, 116, 30);
        this.Controls.Add(this.download);
        var page = new Button { Text = "打开发布页", Bounds = new(142, 286, 112, 30) };
        page.Click += (_, _) => OpenReleasePage(release.Tag);
        this.Controls.Add(page);
        var later = new Button { Text = "稍后", Bounds = new(262, 286, 100, 30) };
        later.Click += (_, _) => this.Close();
        this.Controls.Add(later);
        foreach (var button in new[] { this.download, page, later })
        { button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(40, 90, 118); button.FlatAppearance.BorderSize = 0; }
        this.download.Click += async (_, _) =>
        {
            if (this.downloading) return;
            this.downloading = true;
            this.download.Enabled = false;
            later.Text = "取消";
            this.status.Text = "正在从 GitHub 下载，完成后校验文件…";
            try
            {
                var reporter = new Progress<int>(value => { if (!this.IsDisposed) this.progress.Value = Math.Clamp(value, 0, 100); });
                this.ReadyDirectory = await service.DownloadAsync(release, updateRoot, reporter, this.cancellation.Token);
                this.downloading = false;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (OperationCanceledException) { this.status.Text = this.cancellation.IsCancellationRequested ? "下载已取消，当前版本可继续使用。" : UpdateService.NetworkMessage; }
            catch (HttpRequestException) { this.status.Text = UpdateService.NetworkMessage; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            { this.status.Text = e.Message; }
            finally
            {
                this.downloading = false;
                if (!this.IsDisposed)
                {
                    this.download.Enabled = !this.cancellation.IsCancellationRequested;
                    later.Text = "关闭";
                    if (this.closeRequested) this.Close();
                }
            }
        };
        this.FormClosing += (_, e) =>
        {
            if (!this.downloading) return;
            this.closeRequested = true;
            this.cancellation.Cancel();
            this.status.Text = "正在取消下载…";
            e.Cancel = true;
        };
    }

    internal static void OpenReleasePage(string? tag = null)
    {
        var url = tag is null ? UpdateService.ReleasesPage : UpdateService.ReleasesPage + "/tag/" + Uri.EscapeDataString(tag);
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { MessageBox.Show("无法打开浏览器，请访问：\n" + url, "Moonwake 更新"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { this.cancellation.Cancel(); this.cancellation.Dispose(); }
        base.Dispose(disposing);
    }
}
