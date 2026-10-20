using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HotkeyTriage.App;

/// <summary>
/// 自绘 Tab 条。不用 TabControl 的原因：它的页面区永远是系统白底，
/// 无法与浅灰画布形成层次，也无法把选中态做成下划线样式。
/// </summary>
public sealed class TabBar : Control
{
    private sealed class Item
    {
        public string Title = "";
        public int Count = -1;
        public Rectangle Bounds;
    }

    private readonly List<Item> _items = new();
    private readonly Font _font = UiTheme.Body(9.5F);
    private readonly Font _fontSel = UiTheme.Body(9.5F, FontStyle.Bold);
    private readonly Font _fontBadge = UiTheme.Ui(8F);
    private int _hover = -1;

    public int SelectedIndex { get; private set; }
    public event EventHandler? SelectedIndexChanged;

    public TabBar()
    {
        SetStyle(ControlStyles.UserPaint
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Height = 46;
        BackColor = UiTheme.Surface;
        Font = _font;
        Cursor = Cursors.Hand;
    }

    public void AddTab(string title)
    {
        _items.Add(new Item { Title = title });
        Relayout();
    }

    public void SetBadge(int index, int count)
    {
        if (index < 0 || index >= _items.Count) return;
        _items[index].Count = count;
        Relayout();
        Invalidate();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _items.Count || index == SelectedIndex) return;
        SelectedIndex = index;
        Relayout();
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Relayout()
    {
        int x = 20;
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            var f = i == SelectedIndex ? _fontSel : _font;
            int w = TextRenderer.MeasureText(it.Title, f).Width;
            int badgeW = it.Count >= 0 ? TextRenderer.MeasureText(it.Count.ToString(), _fontBadge).Width + 16 : 0;
            int total = w + badgeW + (badgeW > 0 ? 8 : 0) + 28;
            it.Bounds = new Rectangle(x, 0, total, Height);
            x += total;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            bool sel = i == SelectedIndex;
            bool hot = i == _hover && !sel;

            if (hot)
            {
                var r = new Rectangle(it.Bounds.X + 6, 8, it.Bounds.Width - 12, Height - 18);
                using var path = UiTheme.RoundedRect(r, 6);
                using var b = new SolidBrush(UiTheme.HoverBg);
                g.FillPath(b, path);
            }

            var font = sel ? _fontSel : _font;
            int titleW = TextRenderer.MeasureText(it.Title, font).Width;
            int contentW = titleW;
            int badgeW = 0;
            if (it.Count >= 0)
            {
                badgeW = TextRenderer.MeasureText(it.Count.ToString(), _fontBadge).Width + 14;
                contentW += badgeW + 8;
            }
            int startX = it.Bounds.X + (it.Bounds.Width - contentW) / 2;

            TextRenderer.DrawText(g, it.Title, font,
                new Rectangle(startX, 0, titleW + 4, Height),
                sel ? UiTheme.Ink : UiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (badgeW > 0)
            {
                var br = new Rectangle(startX + titleW + 8, (Height - 18) / 2, badgeW, 18);
                using var path = UiTheme.RoundedRect(br, 9);
                using var b = new SolidBrush(sel ? UiTheme.SelectBg : Color.FromArgb(243, 244, 246));
                g.FillPath(b, path);
                TextRenderer.DrawText(g, it.Count.ToString(), _fontBadge, br,
                    sel ? UiTheme.Accent : UiTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            if (sel)
            {
                var bar = new Rectangle(it.Bounds.X + 14, Height - 3, it.Bounds.Width - 28, 3);
                using var path = UiTheme.RoundedRect(bar, 2);
                using var b = new SolidBrush(UiTheme.Accent);
                g.FillPath(b, path);
            }
        }

        using (var pen = new Pen(UiTheme.Border))
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int idx = _items.FindIndex(i => i.Bounds.Contains(e.Location));
        if (idx != _hover)
        {
            _hover = idx;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int idx = _items.FindIndex(i => i.Bounds.Contains(e.Location));
        if (idx >= 0) Select(idx);
    }
}
