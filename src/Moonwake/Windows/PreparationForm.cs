using Moonwake.Configuration;
using Moonwake.Infrastructure;
using Moonwake.Services;

namespace Moonwake.Windows;

internal sealed class PreparationForm : Form
{
    public StandaloneConfiguration? Configuration { get; private set; }
    public Exception? Failure { get; private set; }

    public PreparationForm(StandalonePaths paths)
    {
        this.Text = "Moonwake";
        this.ClientSize = new Size(400, 128);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.ControlBox = false;
        this.BackColor = Color.FromArgb(15, 23, 31);
        this.ForeColor = Color.FromArgb(190, 227, 242);
        this.Font = new Font("Microsoft YaHei UI", 9F);
        var label = new Label { Text = "正在准备 Moonwake…", Bounds = new Rectangle(24, 24, 350, 40), AutoEllipsis = true };
        this.Controls.Add(label);
        this.Controls.Add(new ProgressBar { Bounds = new Rectangle(24, 82, 352, 6), Style = ProgressBarStyle.Marquee });
        this.Shown += async (_, _) =>
        {
            var progress = new Progress<string>(message => { if (!this.IsDisposed) label.Text = message; });
            try { this.Configuration = await Task.Run(() => DistributionBootstrap.Prepare(paths, ((IProgress<string>)progress).Report)); }
            catch (Exception ex) { this.Failure = ex; }
            this.Close();
        };
    }
}
