using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HotkeyTriage.App;

/// <summary>
/// 卡片顶部的提示条：圆角浅色底 + 左侧强调竖条 + 单行文字。
/// 用于把"下一步该做什么"放在视线最先落到的位置，而不是让用户去读状态栏。
/// </summary>
public sealed class InfoBanner : Control
{
    public Color Tint { get; set; } = Color.FromArgb(26, 39, 64);
    public Color Line { get; set; } = Color.FromArgb(47, 70, 104);
    public Color Bar { get; set; } = UiTheme.Accent;
    public Color Ink { get; set; } = Color.FromArgb(169, 198, 255);

    public InfoBanner()
    {
        SetStyle(ControlStyles.UserPaint
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Height = 42;
        Font = UiTheme.Body(9F);
        BackColor = UiTheme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var r = new Rectangle(0, 2, Width - 1, Height - 6);
        if (r.Width <= 2 || r.Height <= 2) return;

        using (var path = UiTheme.RoundedRect(r, 8))
        {
            using var b = new SolidBrush(Tint);
            g.FillPath(b, path);
            using var p = new Pen(Line);
            g.DrawPath(p, path);
        }

        using (var b = new SolidBrush(Bar))
        {
            using var path = UiTheme.RoundedRect(new Rectangle(r.X + 1, r.Y + 10, 3, r.Height - 20), 2);
            g.FillPath(b, path);
        }

        var textRect = new Rectangle(r.X + 18, r.Y, r.Width - 30, r.Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    public void SetText(string text, bool warn = false)
    {
        Text = text;
        if (warn)
        {
            Tint = Color.FromArgb(43, 34, 22);
            Line = Color.FromArgb(85, 66, 42);
            Bar = UiTheme.Amber;
            Ink = Color.FromArgb(227, 179, 65);
        }
        else
        {
            Tint = Color.FromArgb(26, 39, 64);
            Line = Color.FromArgb(47, 70, 104);
            Bar = UiTheme.Accent;
            Ink = Color.FromArgb(169, 198, 255);
        }
        Invalidate();
    }
}
