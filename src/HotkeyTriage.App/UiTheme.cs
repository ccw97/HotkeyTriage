using System.Drawing;
using System.Drawing.Drawing2D;

namespace HotkeyTriage.App;

/// <summary>
/// 全局视觉常量。深色科技灰主题：低亮度背景 + 高对比文字，
/// 层次靠亮度差（画布 &lt; 卡片 &lt; 悬停/选中）而不是靠描边堆叠。
/// </summary>
public static class UiTheme
{
    // 背景层次：Canvas(最底) < Surface(卡片) < SurfaceRaised(选中/悬停)
    public static readonly Color Canvas        = Color.FromArgb(25, 28, 33);
    public static readonly Color Surface       = Color.FromArgb(34, 38, 45);
    public static readonly Color SurfaceAlt    = Color.FromArgb(22, 25, 29);
    public static readonly Color SurfaceRaised = Color.FromArgb(42, 48, 57);
    public static readonly Color Border        = Color.FromArgb(48, 55, 65);

    // 顶栏
    public static readonly Color HeaderBg      = Color.FromArgb(16, 19, 23);
    public static readonly Color HeaderSub     = Color.FromArgb(133, 143, 158);

    // 文字（深底上保证对比度：主文字 ≈ 12:1）
    public static readonly Color Ink           = Color.FromArgb(232, 236, 242);
    public static readonly Color InkSoft       = Color.FromArgb(202, 210, 220);
    public static readonly Color Muted         = Color.FromArgb(150, 160, 174);
    public static readonly Color Faint         = Color.FromArgb(116, 126, 140);

    // 强调色（深色下需要更高明度才不发闷）
    public static readonly Color Accent        = Color.FromArgb(88, 148, 255);
    public static readonly Color Green         = Color.FromArgb(63, 185, 80);
    public static readonly Color Amber         = Color.FromArgb(210, 153, 34);
    public static readonly Color HoverBg       = Color.FromArgb(40, 46, 55);
    public static readonly Color SelectBg      = Color.FromArgb(38, 51, 76);

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

    /// <summary>画一条水平分隔线。</summary>
    public static void HLine(Graphics g, int x1, int x2, int y, Color color)
    {
        using var pen = new Pen(color);
        g.DrawLine(pen, x1, y, x2, y);
    }
}
