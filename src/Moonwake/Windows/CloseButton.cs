using System.Drawing.Drawing2D;

namespace Moonwake.Windows;

internal sealed class CloseButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var x = (this.ClientSize.Width - 1) / 2f;
        var y = (this.ClientSize.Height - 1) / 2f;
        using var pen = new Pen(this.ForeColor, 1.4f);
        e.Graphics.DrawLine(pen, x - 4, y - 4, x + 4, y + 4);
        e.Graphics.DrawLine(pen, x + 4, y - 4, x - 4, y + 4);
    }
}
