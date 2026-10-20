using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.App;

/// <summary>
/// 深扫确认对话框：列出将被关闭的候选进程，用户勾选确认。
/// Forbidden 进程不在列表中（调用方已过滤）；有窗口的默认不勾选。
/// </summary>
public sealed class DeepScanDialog : Form
{
    private readonly CheckedListBox _list = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Font = UiTheme.Body(9.5F),
        BackColor = UiTheme.Surface,
        ForeColor = UiTheme.InkSoft,
    };

    private readonly RoundButton _ok = new()
    {
        Text = "开始扫描",
        Width = 112,
        Height = 34,
        CornerRadius = 7,
        BackColor = UiTheme.Accent,
    };

    private readonly RoundButton _cancel = new()
    {
        Text = "取消",
        Width = 84,
        Height = 34,
        CornerRadius = 7,
        BackColor = Color.FromArgb(236, 238, 242),
        ForeColor = UiTheme.InkSoft,
    };

    private readonly Panel _banner = new() { Dock = DockStyle.Top, Height = 86 };

    private readonly List<ProcInfo> _all;

    private const string BannerText =
        "扫描过程会临时关闭勾选的程序，结束后自动重新启动。\n" +
        "有窗口的程序默认不勾选 —— 关闭会中断你正在做的事，请谨慎勾选。\n" +
        "explorer.exe 与反作弊进程不会出现在下方列表中。";

    public IReadOnlyList<ProcInfo> ApprovedCandidates { get; private set; }
        = Array.Empty<ProcInfo>();

    public DeepScanDialog(IReadOnlyList<ProcInfo> candidates)
    {
        _all = candidates.ToList();
        Text = "深度扫描";
        Width = 640; Height = 560;
        MinimumSize = new Size(560, 420);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Body(9F);
        Padding = new Padding(16, 12, 16, 12);

        _banner.Paint += PaintBanner;
        _banner.Margin = new Padding(0, 0, 0, 10);

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        card.Controls.Add(_list);

        var btnBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = UiTheme.Canvas,
            Padding = new Padding(0, 12, 0, 0),
        };
        _cancel.Dock = DockStyle.Right;
        _ok.Dock = DockStyle.Right;
        _ok.Margin = new Padding(0, 0, 8, 0);
        btnBar.Controls.Add(_cancel);
        btnBar.Controls.Add(_ok);

        foreach (var p in candidates)
        {
            var label = $"{p.ExeName}     PID {p.Pid}     {(p.HasVisibleWindow ? "有窗口" : "后台")}";
            var idx = _list.Items.Add(label);
            _list.SetItemChecked(idx, !p.HasVisibleWindow);
        }

        _ok.DialogResult = DialogResult.OK;
        _cancel.DialogResult = DialogResult.Cancel;
        _ok.Click += (_, _) =>
        {
            ApprovedCandidates = _all
                .Where((_, i) => i < _list.Items.Count && _list.GetItemChecked(i))
                .ToList();
        };

        // 顺序：Fill → Bottom → Top
        Controls.Add(card);
        Controls.Add(btnBar);
        Controls.Add(_banner);
    }

    private void PaintBanner(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(UiTheme.Canvas);

        var r = new Rectangle(0, 4, _banner.Width - 1, _banner.Height - 10);
        using var path = UiTheme.RoundedRect(r, 8);
        using (var b = new SolidBrush(Color.FromArgb(254, 246, 240)))
            g.FillPath(b, path);
        using (var p = new Pen(Color.FromArgb(247, 214, 191)))
            g.DrawPath(p, path);

        // 左侧竖条强调
        using (var b = new SolidBrush(Color.FromArgb(217, 119, 6)))
            g.FillRectangle(b, r.X + 1, r.Y + 12, 3, r.Height - 24);

        var textRect = new Rectangle(r.X + 18, r.Y + 10, r.Width - 32, r.Height - 16);
        TextRenderer.DrawText(g, BannerText, UiTheme.Body(9F),
            textRect, Color.FromArgb(146, 64, 14),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
            | TextFormatFlags.NoPrefix);
    }
}
