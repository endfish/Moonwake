using Moonwake.Infrastructure;
using Moonwake.Services;

namespace Moonwake.Windows;

internal sealed class ImportForm : Form
{
    private readonly ProfileImportService service;
    private readonly TextBox source = new();
    private readonly Label summary = new();
    private readonly CheckedListBox repositories = new();
    private readonly CheckBox configuration = new();
    private readonly CheckBox development = new();
    private readonly Button import = new();
    private readonly Button browse = new();
    private ImportPlan? plan;
    private bool working;

    public ImportForm(StandalonePaths paths)
    {
        this.service = new ProfileImportService(paths);
        this.Text = "导入插件 — Moonwake";
        this.ClientSize = new Size(520, 435);
        if (Environment.ProcessPath is { } executable) this.Icon = Icon.ExtractAssociatedIcon(executable);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(15, 23, 31);
        this.ForeColor = Color.FromArgb(226, 235, 242);
        this.Font = new Font("Microsoft YaHei UI", 9F);
        this.Controls.Add(new Label { Text = "从已有环境复制插件及配置", Bounds = new Rectangle(24, 18, 470, 28), Font = new Font(this.Font, FontStyle.Bold) });
        this.source.Bounds = new Rectangle(24, 54, 383, 28);
        this.source.ReadOnly = true;
        this.source.BackColor = Color.FromArgb(25, 37, 48);
        this.source.ForeColor = this.ForeColor;
        this.Controls.Add(this.source);
        this.browse.Text = "选择目录";
        this.browse.Bounds = new Rectangle(414, 52, 82, 29);
        this.browse.FlatStyle = FlatStyle.Flat;
        this.browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "选择包含 installedPlugins 和 pluginConfigs 的原环境数据目录", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) this.LoadSource(dialog.SelectedPath);
        };
        this.Controls.Add(this.browse);
        this.summary.Bounds = new Rectangle(24, 94, 472, 42);
        this.Controls.Add(this.summary);
        this.configuration.Text = "同时复制插件配置（保留已有配置）";
        this.configuration.Bounds = new Rectangle(24, 139, 465, 25);
        this.configuration.Checked = true;
        this.Controls.Add(this.configuration);
        this.development.Text = "导入开发插件入口（继续使用原 DLL，关闭自动启动）";
        this.development.Bounds = new Rectangle(24, 168, 472, 25);
        this.development.Checked = true;
        this.Controls.Add(this.development);
        this.Controls.Add(new Label { Text = "选择需要继续使用的仓库地址（默认不导入）", Bounds = new Rectangle(24, 206, 472, 24) });
        this.repositories.Bounds = new Rectangle(24, 233, 472, 91);
        this.repositories.CheckOnClick = true;
        this.repositories.BackColor = this.source.BackColor;
        this.repositories.ForeColor = this.ForeColor;
        this.repositories.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(this.repositories);
        this.Controls.Add(new Label { Text = "配置独立复制，保留已有内容。开发插件引用原构建路径，方便继续开发；导入的插件默认不启动。", Bounds = new Rectangle(24, 336, 472, 37), ForeColor = Color.FromArgb(153, 175, 189) });
        this.import.Text = "导入所选内容";
        this.import.Bounds = new Rectangle(338, 385, 158, 34);
        this.import.BackColor = Color.FromArgb(57, 115, 142);
        this.import.FlatStyle = FlatStyle.Flat;
        this.import.Click += this.ImportAsync;
        this.Controls.Add(this.import);
        this.FormClosing += (_, e) => { if (this.working) e.Cancel = true; };
        var preferred = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncherCN");
        if (Directory.Exists(preferred)) this.LoadSource(preferred);
        else { this.summary.Text = "请选择原环境的数据目录。"; this.import.Enabled = false; }
    }

    private void LoadSource(string directory)
    {
        try
        {
            this.plan = this.service.Inspect(directory);
            this.source.Text = this.plan.SourceRoot;
            this.repositories.Items.Clear();
            foreach (var url in this.plan.Repositories) this.repositories.Items.Add(url, false);
            this.summary.Text = $"可导入 {this.plan.Plugins.Count} 个已安装插件、{this.plan.DevelopmentPlugins.Count} 个开发入口；跳过 {this.plan.SkippedPlugins + this.plan.SkippedDevelopmentPlugins} 个不兼容或不完整项目。";
            this.import.Text = "导入所选内容";
            this.import.Enabled = true;
        }
        catch (Exception ex) { this.summary.Text = ex.Message; this.import.Enabled = false; this.plan = null; }
    }

    private async void ImportAsync(object? sender, EventArgs args)
    {
        if (this.plan is null || this.working) return;
        this.working = true;
        this.import.Enabled = this.browse.Enabled = this.configuration.Enabled = this.development.Enabled = this.repositories.Enabled = false;
        var selected = this.repositories.CheckedItems.Cast<string>().ToArray();
        var includeConfiguration = this.configuration.Checked;
        var includeDevelopment = this.development.Checked;
        try
        {
            var result = await Task.Run(() => this.service.Import(this.plan, includeConfiguration, selected, includeDevelopment));
            this.summary.Text = $"已导入 {result.Plugins} 个插件、{result.DevelopmentPlugins} 个开发入口、{result.ConfigurationItems} 项配置和 {result.Repositories} 个仓库。保留 {result.ExistingPlugins} 个已有插件。";
            this.import.Text = "导入完成";
        }
        catch (Exception ex) { this.summary.Text = "导入失败：" + ex.Message; this.import.Enabled = true; }
        finally { this.working = false; this.browse.Enabled = this.configuration.Enabled = this.development.Enabled = this.repositories.Enabled = true; }
    }
}
