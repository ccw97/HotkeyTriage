using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HotkeyTriage.App;

/// <summary>
/// 圆角白卡片。用于把内容区从浅灰背景上"托起来"，替代 WinForms 默认的一片纯白。
/// </summary>
public sealed class CardPanel : Panel
{
    public int CornerRadius { get; set; } = 10;
    public Color BorderColor { get; set; } = UiTheme.Border;

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Surface;
        Padding = new Padding(10, 8, 10, 8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 先铺父容器底色，再把圆角外的区域"挖掉"
        g.Clear(Parent?.BackColor ?? UiTheme.Canvas);

        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var path = UiTheme.RoundedRect(rect, CornerRadius);
        using (var brush = new SolidBrush(BackColor))
            g.FillPath(brush, path);
        using (var pen = new Pen(BorderColor))
            g.DrawPath(pen, path);
    }
}
