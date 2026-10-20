using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HotkeyTriage.App;

/// <summary>
/// 自绘圆角按钮：hover 高亮、圆角、无边框。
/// WinForms 原生 Button 太素，用于主要操作按钮。
/// </summary>
public sealed class RoundButton : Button
{
    private bool _hover;
    private bool _pressed;

    public int CornerRadius { get; set; } = 6;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        Cursor = Cursors.Hand;
        Height = 32;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true; Invalidate(); base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true; Invalidate(); base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false; Invalidate(); base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.Transparent);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, CornerRadius);

        Color fill = !Enabled ? Color.FromArgb(90, 90, 90)
            : _pressed ? ControlPaint.Dark(BackColor, 0.08f)
            : _hover ? ControlPaint.Light(BackColor, 0.18f)
            : BackColor;

        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        TextRenderer.DrawText(g, Text, Font, rect,
            Enabled ? ForeColor : Color.FromArgb(200, 200, 200),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
