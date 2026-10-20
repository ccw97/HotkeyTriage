using System.Drawing;
using System.Drawing.Drawing2D;

namespace HotkeyTriage.App;

/// <summary>
/// 全局视觉常量。所有颜色/字体集中在此，避免界面各处硬编码出不一致的灰。
/// </summary>
public static class UiTheme
{
    // 背景层次：Canvas(最底) -> Surface(卡片) -> 边框
    public static readonly Color Canvas    = Color.FromArgb(243, 244, 246);
    public static readonly Color Surface   = Color.White;
    public static readonly Color Border    = Color.FromArgb(226, 229, 234);

    // 顶栏
    public static readonly Color HeaderBg  = Color.FromArgb(23, 27, 36);
    public static readonly Color HeaderSub = Color.FromArgb(154, 163, 178);

    // 文字
    public static readonly Color Ink       = Color.FromArgb(17, 24, 39);
    public static readonly Color InkSoft   = Color.FromArgb(55, 65, 81);
    public static readonly Color Muted     = Color.FromArgb(107, 114, 128);
    public static readonly Color Faint     = Color.FromArgb(156, 163, 175);

    // 强调色
    public static readonly Color Accent    = Color.FromArgb(37, 99, 235);
    public static readonly Color Green     = Color.FromArgb(22, 163, 74);
    public static readonly Color HoverBg   = Color.FromArgb(240, 242, 245);
    public static readonly Color SelectBg  = Color.FromArgb(232, 240, 254);

    public static readonly string Family = "Microsoft YaHei UI";

    public static Font Body(float size = 9F, FontStyle style = FontStyle.Regular)
        => new(Family, size, style, GraphicsUnit.Point);

    public static Font Ui(float size = 9F, FontStyle style = FontStyle.Regular)
        => new("Segoe UI", size, style, GraphicsUnit.Point);

    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>画一条水平分隔线（1px，带设备像素对齐）。</summary>
    public static void HLine(Graphics g, int x1, int x2, int y, Color color)
    {
        using var pen = new Pen(color);
        g.DrawLine(pen, x1, y, x2, y);
    }
}
