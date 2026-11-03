using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Models;
using Moonwake.Services;

namespace Moonwake.Windows;

internal sealed class LauncherForm : Form
{
    private static readonly Color Foreground = Color.FromArgb(232, 238, 244);
    private static readonly Color Muted = Color.FromArgb(139, 158, 172);
    private readonly StandaloneConfiguration configuration;
    private readonly GameProcessService processService;
    private readonly InjectionService injectionService;
    private readonly StandaloneLogger logger;
    private readonly PayloadValidator validator;
    private readonly bool preview;
    private readonly ComboBox processes = new ProcessComboBox();
    private readonly Button inject = new PrimaryButton();
    private readonly Label status = new();
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly ToolTip tooltips = new();
    private readonly LinkLabel updateLink = new();
    private readonly UpdateService updates;
    private readonly string updateRoot;
    private AvailableRelease? availableRelease;
    private bool checkingUpdate;
    private bool busy;
    private bool attached;
    private string? lastFailure;

    public LauncherForm(StandaloneConfiguration configuration, GameProcessService processService,
        InjectionService injectionService, StandaloneLogger logger, StandalonePaths paths, bool preview = false)
    {
        this.configuration = configuration;
        this.processService = processService;
        this.injectionService = injectionService;
        this.logger = logger;
        this.validator = new PayloadValidator(paths);
        this.preview = preview;
        this.updates = new UpdateService(paths);
        this.updateRoot = Path.Combine(paths.TempDirectory, "updates");
        this.Text = "Moonwake";
        this.ClientSize = new Size(400, 500);
        this.FormBorderStyle = FormBorderStyle.None;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.Black;
        this.ForeColor = Foreground;
        this.Font = new Font("Microsoft YaHei UI", 9F);
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.DoubleBuffered = true;
        using (var stream = typeof(LauncherForm).Assembly.GetManifestResourceStream("Moonwake.Resources.window-background.png")!)
        using (var source = Image.FromStream(stream)) this.BackgroundImage = new Bitmap(source);
        this.BackgroundImageLayout = ImageLayout.Stretch;
        if (Environment.ProcessPath is { } executable) this.Icon = Icon.ExtractAssociatedIcon(executable);
        this.BuildLayout();
        this.MouseDown += this.DragWindow;
        this.timer.Interval = Math.Clamp(configuration.PollIntervalSeconds, 1, 60) * 1000;
        this.timer.Tick += (_, _) => this.RefreshProcesses();
        this.processes.SelectionChangeCommitted += (_, _) => { this.lastFailure = null; this.UpdateState(); };
        this.inject.Click += this.InjectAsync;
        this.Shown += async (_, _) =>
        {
            this.RefreshProcesses();
            if (!preview) { this.timer.Start(); await this.CheckUpdateAsync(false); }
        };
        this.FormClosing += (_, e) => { if (this.busy) e.Cancel = true; };
        this.FormClosed += (_, _) => { this.lifetime.Cancel(); this.timer.Dispose(); this.tooltips.Dispose(); };
        this.AcceptButton = this.inject;
        if (preview) this.RefreshProcesses();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(this.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    private void BuildLayout()
    {
        var title = new BrandHeader { Bounds = new Rectangle(24, 18, 320, 94), BackColor = Color.Transparent };
        title.MouseDown += this.DragWindow;
        this.Controls.Add(title);
        var close = new CloseButton { Bounds = new Rectangle(355, 17, 30, 30),
            BackColor = Color.Transparent, ForeColor = Muted, FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand, TabStop = false, AccessibleName = "关闭 Moonwake" };
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 48);
        close.FlatAppearance.MouseDownBackColor = Color.FromArgb(54, 39, 45);
        close.Click += (_, _) => this.Close();
        this.Controls.Add(close);

        this.processes.Bounds = new Rectangle(40, 181, 320, 38);
        this.processes.DropDownStyle = ComboBoxStyle.DropDownList;
        this.processes.FlatStyle = FlatStyle.Flat;
        this.processes.DrawMode = DrawMode.OwnerDrawFixed;
        this.processes.ItemHeight = 30;
        this.processes.BackColor = Color.FromArgb(18, 26, 33);
        this.processes.ForeColor = Foreground;
        this.processes.Font = new Font("Microsoft YaHei UI", 9F);
        this.processes.DropDownHeight = 180;
        this.processes.AccessibleName = "游戏进程";
        this.processes.DrawItem += (_, e) =>
        {
            using var fill = new SolidBrush((e.State & DrawItemState.Selected) != 0 && this.processes.DroppedDown
                ? Color.FromArgb(35, 53, 64) : this.processes.BackColor);
            e.Graphics.FillRectangle(fill, e.Bounds);
            var text = e.Index >= 0 ? this.processes.GetItemText(this.processes.Items[e.Index]) : "等待游戏进程…";
            TextRenderer.DrawText(e.Graphics, text, this.processes.Font,
                new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height), Foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        this.Controls.Add(this.processes);
        this.inject.Text = "注入";
        this.inject.Bounds = new Rectangle(40, 233, 320, 42);
        this.inject.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        this.inject.Cursor = Cursors.Hand;
        this.Controls.Add(this.inject);
        this.status.Bounds = new Rectangle(40, 289, 320, 34);
        this.status.BackColor = Color.Transparent;
        this.status.ForeColor = Muted;
        this.status.Font = new Font("Microsoft YaHei UI", 8F);
        this.status.TextAlign = ContentAlignment.TopCenter;
        this.status.AutoEllipsis = true;
        this.Controls.Add(this.status);
        this.tooltips.SetToolTip(this.inject, "成功后隐藏窗口；游戏关闭时自动退出，无托盘图标。");
        this.updateLink.Bounds = new Rectangle(40, 465, 320, 24);
        this.updateLink.Text = $"v{UpdateService.CurrentVersion} · 检查更新";
        this.updateLink.TextAlign = ContentAlignment.MiddleCenter;
        this.updateLink.BackColor = Color.Transparent;
        this.updateLink.LinkColor = this.updateLink.ActiveLinkColor = Color.FromArgb(168, 214, 235);
        this.updateLink.Font = new Font("Microsoft YaHei UI", 8F);
        this.updateLink.LinkBehavior = LinkBehavior.HoverUnderline;
        this.updateLink.LinkClicked += async (_, _) =>
        {
            if (this.busy || this.attached || this.checkingUpdate || this.preview) return;
            if (this.availableRelease is null) await this.CheckUpdateAsync(true);
            if (this.availableRelease is not { } release || this.IsDisposed || this.attached || this.busy) return;
            if (this.processService.Discover().Any(p => p.DalamudState == DalamudModuleState.Loaded))
            { MessageBox.Show(this, "请在退出已加载 Dalamud 的游戏后更新 Moonwake。", "Moonwake 更新"); return; }
            this.busy = true;
            this.inject.Enabled = false;
            try
            {
                using var dialog = new UpdateForm(release, this.updates, this.updateRoot);
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ReadyDirectory is null)
                { this.availableRelease = null; return; }
                if (this.processService.Discover().Any(p => p.DalamudState == DalamudModuleState.Loaded))
                    throw new InvalidOperationException("检测到游戏已加载核心，请退出游戏后再更新。");
                UpdateInstaller.Begin(dialog.ReadyDirectory);
                this.busy = false;
                this.Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { this.logger.Warn("更新未完成：" + ex.Message); MessageBox.Show(this, ex.Message, "Moonwake 更新"); }
            finally { this.busy = false; if (!this.IsDisposed) this.UpdateState(); }
        };
        this.Controls.Add(this.updateLink);
    }

    private async Task CheckUpdateAsync(bool manual)
    {
        if (this.checkingUpdate) return;
        this.checkingUpdate = true;
        this.updateLink.Text = $"v{UpdateService.CurrentVersion} · 正在检查更新…";
        try
        {
            var result = await this.updates.CheckAsync(manual, this.lifetime.Token);
            if (this.IsDisposed || this.attached) return;
            this.availableRelease = result.Release;
            this.updateLink.Text = result.Release is { } release ? $"发现新版本 {release.Tag} · 查看更新" :
                $"v{UpdateService.CurrentVersion} · {(result.Error is null ? "检查更新" : "暂时无法连接 GitHub")}";
            this.tooltips.SetToolTip(this.updateLink, result.Error ?? "自动检查每天一次；点击可手动检查更新。可使用系统网络代理。");
            if (result.Error is not null) this.logger.Warn(result.Error);
            if (manual && result.Release is null) MessageBox.Show(this, result.Error ?? "当前已是此渠道的最新版本。", "Moonwake 更新");
        }
        catch (OperationCanceledException) when (this.lifetime.IsCancellationRequested) { }
        finally { this.checkingUpdate = false; }
    }

    private void SetStatus(string text)
    {
        this.status.Text = text;
        this.tooltips.SetToolTip(this.status, text);
    }

    private void RefreshProcesses()
    {
        if (this.busy || this.attached || this.processes.DroppedDown) return;
        if (this.preview)
        {
            this.processes.Items.Clear();
            this.processes.Items.Add("FINAL FANTASY XIV  ·  进程 12840");
            this.processes.SelectedIndex = 0;
            this.SetStatus("注入后隐藏窗口，游戏退出时自动关闭。");
            this.inject.Enabled = true;
            return;
        }
        try
        {
            var selectedId = (this.processes.SelectedItem as ProcessItem)?.Process.ProcessId;
            var discovered = this.processService.Discover();
            this.processes.BeginUpdate();
            try
            {
                this.processes.Items.Clear();
                foreach (var process in discovered) this.processes.Items.Add(new ProcessItem(process));
                if (discovered.Count > 0)
                {
                    var selectedIndex = discovered.ToList().FindIndex(p => p.ProcessId == selectedId);
                    this.processes.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                }
                else { this.processes.Items.Add("等待游戏进程…"); this.processes.SelectedIndex = 0; }
            }
            finally { this.processes.EndUpdate(); }
            this.UpdateState();
        }
        catch (Exception ex) { this.SetStatus("无法读取游戏进程：" + ex.Message); this.inject.Enabled = false; }
    }

    private void UpdateState()
    {
        if (this.preview || this.busy || this.attached) return;
        var process = (this.processes.SelectedItem as ProcessItem)?.Process;
        this.tooltips.SetToolTip(this.processes, $"游戏版本：{process?.GameVersion ?? "未知"}\n{process?.ExecutablePath}");
        var readiness = LauncherReadiness.Evaluate(process, this.configuration, this.validator.Validate(this.configuration).IsValid);
        this.inject.Text = "注入";
        this.inject.Enabled = readiness.CanInject;
        this.SetStatus(readiness.CanInject ? this.lastFailure ?? readiness.Message : readiness.Message);
    }

    private async void InjectAsync(object? sender, EventArgs args)
    {
        if (this.preview || this.busy || this.attached || this.processes.SelectedItem is not ProcessItem selected) return;
        this.lastFailure = null;
        this.busy = true;
        this.timer.Stop();
        this.inject.Enabled = this.processes.Enabled = false;
        this.inject.Text = "正在注入…";
        this.SetStatus("正在连接所选游戏进程。");
        var completed = false;
        try
        {
            var target = this.processService.Find(selected.Process.ProcessId) ?? throw new InvalidOperationException("所选游戏已退出。");
            if (target.StartTimeUtcTicks != selected.Process.StartTimeUtcTicks) throw new InvalidOperationException("游戏进程已变化，请重新选择。");
            if (target.GameVersion != selected.Process.GameVersion) throw new InvalidOperationException("游戏版本已变化，请重新选择。");
            using var session = GameSession.Capture(target);
            var result = await this.injectionService.InjectAsync(target, this.configuration, false, false, this.lifetime.Token);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            var loaded = false;
            for (var attempt = 0; attempt < 10 && !session.HasExited; attempt++)
            {
                if (this.processService.Find(target.ProcessId)?.DalamudState == DalamudModuleState.Loaded) { loaded = true; break; }
                await Task.Delay(500, this.lifetime.Token);
            }
            if (!loaded) throw new InvalidOperationException("尚未确认核心模块加载，请查看日志；不会自动重试。");
            this.attached = true;
            this.busy = false;
            this.logger.Info($"Moonwake is following game instance {target.ProcessId}; no tray icon is created.");
            this.Hide();
            this.ShowInTaskbar = false;
            await session.WaitForExitAsync(this.lifetime.Token);
            this.logger.Info("目标游戏已退出，Moonwake 自动结束。");
            completed = true;
            this.Close();
        }
        catch (OperationCanceledException) when (this.lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            this.logger.Error(ex.ToString());
            this.lastFailure = ex.Message;
            this.SetStatus(ex.Message);
            if (this.attached) { this.attached = false; this.ShowInTaskbar = true; this.Show(); }
        }
        finally
        {
            this.busy = false;
            if (!completed && !this.IsDisposed && !this.attached)
            {
                this.processes.Enabled = true;
                this.inject.Text = "注入";
                this.timer.Start();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { this.BackgroundImage?.Dispose(); this.updates.Dispose(); this.lifetime.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed record ProcessItem(GameProcessInfo Process)
    {
        public override string ToString() => $"FINAL FANTASY XIV  ·  进程 {this.Process.ProcessId}";
    }

    private sealed class ProcessComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is not (0xF or 0x317 or 0x318)) return;
            using var graphics = m.Msg == 0xF ? Graphics.FromHwnd(this.Handle) : Graphics.FromHdc(m.WParam);
            var arrowWidth = (int)(30 * this.DeviceDpi / 96F);
            using var fill = new SolidBrush(this.BackColor);
            graphics.FillRectangle(fill, this.Width - arrowWidth, 0, arrowWidth, this.Height);
            graphics.FillRectangle(fill, 0, 0, this.Width, 2);
            graphics.FillRectangle(fill, 0, this.Height - 2, this.Width, 2);
            graphics.FillRectangle(fill, 0, 0, 2, this.Height);
            using var border = new Pen(Color.FromArgb(45, 61, 71));
            graphics.DrawRectangle(border, 0, 0, this.Width - 1, this.Height - 1);
            using var line = new Pen(Muted, Math.Max(1, this.DeviceDpi / 96F));
            var x = this.Width - arrowWidth / 2F;
            var y = this.Height / 2F;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawLines(line, new PointF[] { new(x - 4, y - 2), new(x, y + 2), new(x + 4, y - 2) });
        }
    }

    private sealed class PrimaryButton : Button
    {
        public PrimaryButton() { this.FlatStyle = FlatStyle.Flat; this.FlatAppearance.BorderSize = 0; this.DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var hovered = this.ClientRectangle.Contains(this.PointToClient(Cursor.Position));
            var color = !this.Enabled ? Color.FromArgb(31, 45, 55) : hovered ? Color.FromArgb(61, 128, 157) : Color.FromArgb(40, 90, 118);
            using var gradient = new LinearGradientBrush(this.ClientRectangle, color, Color.FromArgb(color.R / 2, color.G / 2, color.B / 2), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(gradient, this.ClientRectangle);
            using var edge = new Pen(this.Enabled ? Color.FromArgb(134, 166, 179) : Color.FromArgb(70, 88, 100));
            e.Graphics.DrawRectangle(edge, 0, 0, this.Width - 1, this.Height - 1);
            using var inner = new Pen(Color.FromArgb(48, 219, 205, 164));
            e.Graphics.DrawLine(inner, 8, 2, this.Width - 9, 2);
            TextRenderer.DrawText(e.Graphics, this.Text, this.Font, this.ClientRectangle,
                this.Enabled ? Foreground : Color.FromArgb(137, 156, 165),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (this.Focused && this.ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(this.ClientRectangle, -4, -4));
        }
    }

    private sealed class BrandHeader : Control
    {
        private readonly Image mark;

        public BrandHeader()
        {
            this.SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            using var stream = typeof(LauncherForm).Assembly.GetManifestResourceStream("Moonwake.Resources.app-icon.png")!;
            using var source = Image.FromStream(stream);
            this.mark = new Bitmap(source);
            this.AccessibleName = "Moonwake";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.ScaleTransform(this.Width / 320F, this.Height / 94F);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawImage(this.mark, new Rectangle(0, 0, 86, 86));
            using var font = new Font("Georgia", 18F, FontStyle.Regular, GraphicsUnit.Point);
            using var ink = new SolidBrush(Color.FromArgb(229, 234, 232));
            using var format = new StringFormat(StringFormat.GenericTypographic);
            var x = 92F;
            foreach (var character in "MOONWAKE")
            {
                var text = character.ToString();
                g.DrawString(text, font, ink, x, 27, format);
                x += g.MeasureString(text, font, PointF.Empty, format).Width + 1.8F;
            }
            using var rule = new Pen(Color.FromArgb(145, 126, 91), 0.8F);
            g.DrawLine(rule, 94, 65, 301, 65);
            using var gold = new SolidBrush(Color.FromArgb(185, 164, 121));
            g.FillPolygon(gold, new PointF[] { new(193, 62), new(196, 65), new(193, 68), new(190, 65) });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) this.mark.Dispose();
            base.Dispose(disposing);
        }
    }
}
