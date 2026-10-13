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
    private static readonly Color Accent = Color.FromArgb(158, 218, 221);
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
        this.Shown += (_, _) => { this.RefreshProcesses(); if (!preview) this.timer.Start(); };
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
        var title = new Label { Text = "Moonwake", Bounds = new Rectangle(28, 24, 255, 42),
            BackColor = Color.Transparent, ForeColor = Foreground, Font = new Font("Segoe UI", 21F, FontStyle.Bold) };
        title.MouseDown += this.DragWindow;
        this.Controls.Add(title);
        var close = new Button { Text = "×", Bounds = new Rectangle(355, 17, 30, 30),
            BackColor = Color.Transparent, ForeColor = Muted, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 16F), Cursor = Cursors.Hand, TabStop = false, AccessibleName = "关闭 Moonwake" };
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
            this.SetStatus("连接成功后，随游戏一同退出。");
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
        this.inject.Enabled = false;
        if (this.processes.SelectedItem is not ProcessItem item)
        {
            this.SetStatus("启动游戏后，将自动选择进程。");
            return;
        }
        var process = item.Process;
        this.tooltips.SetToolTip(this.processes, $"游戏版本：{process.GameVersion ?? "未知"}\n{process.ExecutablePath}");
        if (process.DalamudState == DalamudModuleState.Loaded)
            this.SetStatus("此进程已加载 Dalamud。");
        else if (process.DalamudState == DalamudModuleState.Unknown)
            this.SetStatus("无法读取模块，请检查与游戏的运行权限。");
        else if (process.GameVersion is null || !this.configuration.SupportedGameVersions.Contains(process.GameVersion, StringComparer.Ordinal))
            this.SetStatus("当前游戏版本尚未通过验证。");
        else if (!this.validator.Validate(this.configuration).IsValid)
            this.SetStatus("本地核心与运行资源尚未准备完成。");
        else
        {
            this.SetStatus(this.lastFailure ?? "连接成功后，随游戏一同退出。");
            this.inject.Enabled = true;
        }
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
        if (disposing) { this.BackgroundImage?.Dispose(); this.lifetime.Dispose(); }
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
            var color = !this.Enabled ? Color.FromArgb(43, 64, 72) : hovered ? Color.FromArgb(190, 237, 238) : Accent;
            e.Graphics.Clear(color);
            TextRenderer.DrawText(e.Graphics, this.Text, this.Font, this.ClientRectangle,
                this.Enabled ? Color.FromArgb(18, 39, 45) : Color.FromArgb(137, 156, 165),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (this.Focused && this.ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(this.ClientRectangle, -4, -4));
        }
    }
}
