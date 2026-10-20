using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using HotkeyTriage.Core.Act;
using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Discover;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.App;

/// <summary>
/// 主窗口：双 Tab + 树状视图 + 快扫/深扫/快速识别。
///
/// 视觉结构（四层，形成层次）：
///   深色顶栏(标题+操作) → 白色 Tab 条 → 浅灰画布 + 白色圆角卡片 → 底部状态条
/// </summary>
public sealed class MainForm : Form
{
    // ── 布局 ───────────────────────────────────────────────
    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 64 };
    private readonly Label _title = new() { AutoSize = true };
    private readonly Label _subtitle = new() { AutoSize = true };
    private readonly FlowLayoutPanel _actions = new();
    private readonly RoundButton _deepScanBtn = new() { Text = "深度扫描", Width = 100 };
    private readonly RoundButton _fastBtn = new() { Text = "快速识别", Width = 100 };
    private readonly TabBar _tabBar = new() { Dock = DockStyle.Top };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Panel _statusBar = new() { Dock = DockStyle.Bottom, Height = 30 };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Fill };

    private readonly CardPanel _sysCard = new() { Dock = DockStyle.Fill };
    private readonly CardPanel _appCard = new() { Dock = DockStyle.Fill };
    private readonly TreeView _sysTree = new() { Dock = DockStyle.Fill };
    private readonly TreeView _appTree = new() { Dock = DockStyle.Fill };
    private readonly Label _sysEmpty = new() { Dock = DockStyle.Fill };
    private readonly Label _appEmpty = new() { Dock = DockStyle.Fill };
    private readonly InfoBanner _appBanner = new() { Dock = DockStyle.Top, Height = 48 };

    // ── 绘制资源（复用，避免每次重绘 new Font）───────────────
    private readonly Font _fontGroup = UiTheme.Body(9.5F, FontStyle.Bold);
    private readonly Font _fontKey = UiTheme.Ui(9.5F);
    private readonly Font _fontSub = UiTheme.Body(9F);

    // ── 业务 ───────────────────────────────────────────────
    private readonly HotkeyProbe _probe = new();
    private readonly ProcessScanner _procScanner = new();
    private readonly ProcessController _ctrl = new();
    private readonly DisabledListStore _store = new();

    private HotkeyMap? _map;
    private List<ProcInfo> _lastCandidates = new();

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "hotkey_triage.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n");
        }
        catch { }
    }

    public MainForm()
    {
        Text = "HotkeyTriage 全局热键管理器";
        Width = 960; Height = 660;
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Canvas;
        Font = UiTheme.Body(9F);
        DoubleBuffered = true;

        BuildHeader();
        BuildTabs();
        BuildContent();
        BuildStatusBar();

        // Dock 布局按 z-order 逆序处理：Fill 先加，最后被挤到剩余空间
        Controls.Add(_content);
        Controls.Add(_tabBar);
        Controls.Add(_statusBar);
        Controls.Add(_header);

        _appTree.NodeMouseDoubleClick += OnAppNodeDoubleClick;
        _tabBar.SelectedIndexChanged += (_, _) => SwitchTab();

        Load += async (_, _) => { Log("Form Load 触发"); await QuickScanAsync(); };
    }

    // ── 构建视觉 ───────────────────────────────────────────
    private void BuildHeader()
    {
        _header.BackColor = UiTheme.HeaderBg;
        _header.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(45, 52, 66));
            e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
        };

        _title.Text = "全局热键管理器";
        _title.Font = UiTheme.Body(13.5F, FontStyle.Bold);
        _title.ForeColor = Color.White;
        _title.Location = new Point(22, 11);
        _title.BackColor = Color.Transparent;

        _subtitle.Text = "不注入进程 · 不装驱动 · 结束进程可恢复";
        _subtitle.Font = UiTheme.Body(8.5F);
        _subtitle.ForeColor = UiTheme.HeaderSub;
        _subtitle.Location = new Point(23, 38);
        _subtitle.BackColor = Color.Transparent;

        _deepScanBtn.BackColor = UiTheme.Accent;
        _deepScanBtn.CornerRadius = 7;
        _deepScanBtn.Height = 32;
        _deepScanBtn.Click += OnDeepScan;

        _fastBtn.BackColor = UiTheme.Green;
        _fastBtn.CornerRadius = 7;
        _fastBtn.Height = 32;
        _fastBtn.Click += OnFastIdentify;

        _actions.Dock = DockStyle.Right;
        _actions.FlowDirection = FlowDirection.RightToLeft;
        _actions.WrapContents = false;
        _actions.AutoSize = true;
        _actions.BackColor = UiTheme.HeaderBg;
        _actions.Padding = new Padding(0, 16, 16, 0);
        _actions.Controls.Add(_deepScanBtn);
        _actions.Controls.Add(_fastBtn);

        _header.Controls.Add(_actions);
        _header.Controls.Add(_title);
        _header.Controls.Add(_subtitle);
    }

    private void BuildTabs()
    {
        _tabBar.AddTab("Windows 自带");
        _tabBar.AddTab("其他应用");
    }

    private void BuildContent()
    {
        _content.BackColor = UiTheme.Canvas;
        _content.Padding = new Padding(14, 12, 14, 12);

        StyleTree(_sysTree);
        StyleTree(_appTree);
        _sysTree.DrawNode += DrawNode;
        _appTree.DrawNode += DrawNode;

        StyleEmpty(_sysEmpty, "正在扫描系统热键…");
        StyleEmpty(_appEmpty, "未检测到第三方占用");

        _sysCard.Controls.Add(_sysTree);
        _sysCard.Controls.Add(_sysEmpty);

        // 顺序：Fill 先加（树 → 空提示覆盖其上），Top 最后加
        _appCard.Controls.Add(_appTree);
        _appCard.Controls.Add(_appEmpty);
        _appCard.Controls.Add(_appBanner);
        _appBanner.SetText("正在读取热键占用情况…");
        _appCard.Visible = false;

        _content.Controls.Add(_appCard);
        _content.Controls.Add(_sysCard);
    }

    private void BuildStatusBar()
    {
        _statusBar.BackColor = Color.FromArgb(250, 250, 251);
        _statusBar.Paint += (_, e) =>
        {
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, _statusBar.Width, 0);
        };

        _status.Text = "就绪";
        _status.Font = UiTheme.Body(8.5F);
        _status.ForeColor = UiTheme.Muted;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(18, 0, 12, 0);
        _statusBar.Controls.Add(_status);
    }

    private static void StyleTree(TreeView tv)
    {
        tv.BorderStyle = BorderStyle.None;
        tv.BackColor = UiTheme.Surface;
        tv.ForeColor = UiTheme.InkSoft;
        tv.Font = UiTheme.Body(9F);
        tv.DrawMode = TreeViewDrawMode.OwnerDrawText;
        tv.FullRowSelect = true;
        tv.HideSelection = false;
        tv.ShowLines = false;
        tv.ShowRootLines = false;
        tv.ShowPlusMinus = true;
        tv.ItemHeight = 30;
        tv.Indent = 18;
        tv.Scrollable = true;
    }

    private static void StyleEmpty(Label l, string text)
    {
        l.Text = text;
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.ForeColor = UiTheme.Faint;
        l.BackColor = UiTheme.Surface;
        l.Font = UiTheme.Body(9.5F);
    }

    private void SwitchTab()
    {
        bool sys = _tabBar.SelectedIndex == 0;
        _sysCard.Visible = sys;
        _appCard.Visible = !sys;
        if (sys) _sysCard.BringToFront(); else _appCard.BringToFront();
    }

    /// <summary>
    /// 节点文本用 '\t' 分段：[主文本] \t [次要] \t [更次要]。
    /// 主文本固定列宽，保证键名纵向对齐；次要文本灰色，形成信息层次。
    /// </summary>
    private void DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node == null) return;
        var tv = (TreeView)sender!;
        var g = e.Graphics;
        bool selected = (e.State & TreeNodeStates.Selected) != 0;
        bool isGroup = e.Node.Level == 0;

        var parts = e.Node.Text.Split('\t');
        var mainFont = isGroup ? _fontGroup : _fontKey;
        var mainColor = selected ? Color.White : (isGroup ? UiTheme.Ink : UiTheme.InkSoft);
        var subColor = selected ? Color.FromArgb(216, 228, 250) : UiTheme.Muted;
        var faintColor = selected ? Color.FromArgb(196, 212, 244) : UiTheme.Faint;

        const int FLAGS = (int)(TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                               | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        int x = e.Bounds.X;
        int y = e.Bounds.Y;
        int h = e.Bounds.Height;

        // 主列：分组自适应，叶子固定 150px 对齐
        int mainW = isGroup
            ? TextRenderer.MeasureText(g, parts[0], mainFont).Width + 12
            : 150;
        mainW = Math.Min(mainW, Math.Max(40, tv.ClientSize.Width - x - 8));
        TextRenderer.DrawText(g, parts[0], mainFont, new Rectangle(x, y, mainW, h), mainColor,
            (TextFormatFlags)FLAGS);
        x += mainW;

        for (int i = 1; i < parts.Length && x < tv.ClientSize.Width - 4; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;
            var f = _fontSub;
            var c = i >= 2 ? faintColor : subColor;
            int w = TextRenderer.MeasureText(g, parts[i], f).Width + 10;
            w = Math.Min(w, Math.Max(20, tv.ClientSize.Width - x - 4));
            TextRenderer.DrawText(g, parts[i], f, new Rectangle(x, y, w, h), c,
                (TextFormatFlags)FLAGS);
            x += w + 8;
        }
    }

    // ── 业务：快扫 ─────────────────────────────────────────
    private async Task QuickScanAsync()
    {
        Log("QuickScan 开始");
        SetStatus("正在扫描系统热键…");
        _deepScanBtn.Enabled = false;
        _fastBtn.Enabled = false;
        try
        {
            var scanner = new QuickScanner(_probe);
            _map = await Task.Run(() => scanner.Scan());
            Log($"QuickScan 完成: {_map.GridSize} 网格, {_map.Entries.Count} 命中");
            FillSystemTree();
            FillAppTreeQuick();
            var third = _map.Entries.Count(e => e.Kind == HotkeyKind.ThirdParty);
            SetStatus($"已扫描 {_map.GridSize} 个组合，命中 {_map.Entries.Count} 个（第三方待归因 {third}）");
        }
        catch (Exception ex)
        {
            Log($"QuickScan 失败: {ex}");
            SetStatus("快扫失败");
            MessageBox.Show(this,
                $"快扫失败：{ex.Message}\n\n详见日志：\n{Path.GetTempPath()}hotkey_triage.log",
                "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _deepScanBtn.Enabled = true;
            _fastBtn.Enabled = true;
        }
    }

    private void FillSystemTree()
    {
        _sysTree.BeginUpdate();
        _sysTree.Nodes.Clear();
        var sysEntries = _map!.Entries.Where(e => e.Kind == HotkeyKind.System).ToList();

        // 排序原则：能关的排最前（用户最可能动手），纯壳热键排最后（信息量大但价值低）。
        // 折叠原则：大组默认收起，否则 100+ 条壳热键会把真正有用的 5 条挤出首屏。
        var groups = sysEntries
            .GroupBy(e => e.Group)
            .Select(g => new
            {
                Name = g.Key,
                Items = g.OrderBy(x => x.Combo).ToList(),
                HasHint = g.Any(x => !string.IsNullOrEmpty(x.Hint)),
            })
            .OrderBy(g => g.Name == "系统壳热键" ? 2 : g.HasHint ? 0 : 1)
            .ThenBy(g => g.Name)
            .ToList();

        foreach (var g in groups)
        {
            var groupNode = _sysTree.Nodes.Add($"{g.Name}\t{g.Items.Count} 项");
            foreach (var e in g.Items)
            {
                var text = $"{e.Combo}\t{e.Description}";
                if (!string.IsNullOrEmpty(e.Hint)) text += $"\t{e.Hint}";
                var n = groupNode.Nodes.Add(text);
                n.ToolTipText = string.IsNullOrEmpty(e.Hint) ? e.Description : $"{e.Description}\n{e.Hint}";
            }

            if (g.HasHint || g.Items.Count <= 12) groupNode.Expand();
        }
        _sysTree.EndUpdate();

        _tabBar.SetBadge(0, sysEntries.Count);
        _sysEmpty.Visible = sysEntries.Count == 0;
        if (sysEntries.Count == 0) _sysEmpty.Text = "未检测到 Windows 自带热键";
    }

    private void FillAppTreeQuick()
    {
        _appTree.BeginUpdate();
        _appTree.Nodes.Clear();
        var third = _map!.Entries.Where(e => e.Kind == HotkeyKind.ThirdParty).ToList();

        _tabBar.SetBadge(1, third.Count);

        if (third.Count == 0)
        {
            _appBanner.Visible = false;
            _appBanner.SetText("");
            _appEmpty.Visible = true;
            _appEmpty.Text = "未检测到第三方软件占用热键";
            _appTree.EndUpdate();
            return;
        }

        _appEmpty.Visible = false;
        _appBanner.Visible = true;
        _appBanner.SetText(
            $"{third.Count} 个热键归属未知 —— 点右上角「深度扫描」定位到具体程序（会临时关闭程序，可自动恢复）");

        var root = _appTree.Nodes.Add($"待归因\t{third.Count} 个热键");
        foreach (var e in third.OrderBy(x => x.Combo))
            root.Nodes.Add($"{e.Combo}\t所属程序未知");
        root.Expand();
        _appTree.EndUpdate();
    }

    // ── 业务：深扫 ─────────────────────────────────────────
    private async void OnDeepScan(object? sender, EventArgs e)
    {
        if (_map == null) return;
        var takenKeys = _map.Entries
            .Where(e => e.Kind == HotkeyKind.ThirdParty)
            .Select(e => (e.Mods, e.Vk))
            .ToList();
        if (takenKeys.Count == 0) return;

        var candidates = _procScanner.Scan()
            .Where(p => Guard.Evaluate(p) != RiskLevel.Forbidden)
            .Where(p => p.Pid != Environment.ProcessId)
            .OrderByDescending(p => p.HasVisibleWindow ? 30 : 0)
            .ToList();
        _lastCandidates = candidates;

        using var dlg = new DeepScanDialog(candidates);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var approved = dlg.ApprovedCandidates;
        if (approved.Count == 0) return;

        _deepScanBtn.Enabled = false;
        _fastBtn.Enabled = false;
        SetStatus("深扫中…");

        var deep = new DeepScanner(_probe, _ctrl, _store);
        var progress = new Progress<DeepScanProgress>(p =>
            SetStatus($"深扫 {p.Processed}/{p.Total} · 当前 {p.CurrentExe} · 剩余 {p.RemainingKeys} 个键"));

        try
        {
            var result = await Task.Run(() => deep.Scan(approved, takenKeys, progress));
            FillAppTreeDeep(result);
            SetStatus(result.Completed
                ? $"深扫完成，归因到 {result.AttributionByPid.Count} 个进程"
                : $"深扫完成（部分），归因 {result.AttributionByPid.Count}，未归因 {result.Unattributed.Count}");
        }
        catch (OperationCanceledException)
        {
            SetStatus("深扫已中止");
        }
        finally
        {
            _deepScanBtn.Enabled = true;
            _fastBtn.Enabled = true;
        }
    }

    private void FillAppTreeDeep(DeepScanResult result)
    {
        FillAppTreeFromAttribution(result.AttributionByPid, result.Unattributed,
            "占用者可能在未勾选的进程中（有窗口的默认未勾选）");
    }

    // ── 业务：快速识别 ─────────────────────────────────────
    private async void OnFastIdentify(object? sender, EventArgs e)
    {
        if (_map == null) return;
        var takenKeys = _map.Entries
            .Where(e => e.Kind == HotkeyKind.ThirdParty)
            .Select(e => (e.Mods, e.Vk))
            .ToList();
        if (takenKeys.Count == 0) return;

        var candidates = _procScanner.Scan().ToList();
        _lastCandidates = candidates;

        if (candidates.Any(p => Guard.IsAntiCheat(p.ExeName)))
        {
            MessageBox.Show(this, "检测到反作弊进程正在运行，快速识别已禁用以保账号安全。\n请改用「深度扫描」。",
                "已拒绝", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            "快速识别会模拟按下每个被占用的热键，因此会触发占用者的功能。\n" +
            "（微信会弹截图框、录屏开始录、音乐切歌）\n\n" +
            "不会关闭任何程序。过程中请勿操作键盘。\n\n继续？",
            "快速识别", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _fastBtn.Enabled = false;
        _deepScanBtn.Enabled = false;
        SetStatus("快速识别中，正在模拟按键…");

        try
        {
            using var fast = new FastIdentify();
            var result = await Task.Run(() => fast.Identify(takenKeys, candidates));
            if (result.Refused)
            {
                MessageBox.Show(this, result.Reason, "快速识别被拒绝",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("快速识别被拒绝：" + result.Reason);
            }
            else
            {
                FillAppTreeFast(result);
                SetStatus(result.Completed
                    ? $"快速识别完成，归因到 {result.AttributionByPid.Count} 个进程"
                    : $"快速识别完成（部分），归因 {result.AttributionByPid.Count}，未归因 {result.Unattributed.Count}");
            }
        }
        catch (Exception ex)
        {
            Log($"快速识别失败: {ex}");
            MessageBox.Show(this, $"快速识别失败：{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _fastBtn.Enabled = true;
            _deepScanBtn.Enabled = true;
        }
    }

    private void FillAppTreeFast(FastIdentifyResult result)
    {
        FillAppTreeFromAttribution(result.AttributionByPid, result.Unattributed,
            "占用者可能是钩子型（AHK/录屏）或受保护进程，这两类无法用本方法识别");
    }

    private void FillAppTreeFromAttribution(
        IReadOnlyDictionary<int, List<(uint Mods, uint Vk)>> attribution,
        IReadOnlyList<(uint Mods, uint Vk)> unattributed,
        string hint)
    {
        _appTree.BeginUpdate();
        _appTree.Nodes.Clear();

        foreach (var kv in attribution.OrderByDescending(k => k.Value.Count))
        {
            var proc = _lastCandidates.FirstOrDefault(p => p.Pid == kv.Key);
            var name = proc?.ExeName ?? $"PID {kv.Key}";
            var node = _appTree.Nodes.Add($"{name}\tPID {kv.Key}\t{kv.Value.Count} 个热键");
            node.Tag = kv.Key;
            node.ToolTipText = proc?.FullPath ?? "";
            node.Expand();
            foreach (var key in kv.Value)
                node.Nodes.Add($"{KeyCombo.Format(key.Mods, key.Vk)}\t{name}");
        }

        if (unattributed.Count > 0)
        {
            var unk = _appTree.Nodes.Add($"无法归因\t{unattributed.Count} 个热键");
            unk.Nodes.Add($"说明\t{hint}");
            foreach (var key in unattributed)
                unk.Nodes.Add($"{KeyCombo.Format(key.Mods, key.Vk)}\t来源未知");
            unk.Expand();
        }

        _appTree.ExpandAll();
        _appTree.EndUpdate();

        bool nothing = attribution.Count == 0 && unattributed.Count == 0;
        _appEmpty.Visible = nothing;
        _appBanner.Visible = !nothing;

        if (nothing)
        {
            _appEmpty.Text = "未找到可归因的热键";
            return;
        }

        int total = attribution.Sum(k => k.Value.Count);
        if (unattributed.Count > 0)
            _appBanner.SetText(
                $"已归因 {attribution.Count} 个进程 / {total} 个热键，另有 {unattributed.Count} 个未归因 —— {hint}",
                warn: true);
        else
            _appBanner.SetText($"已归因 {attribution.Count} 个进程 / {total} 个热键");
    }

    // ── 业务：结束后可恢复 ─────────────────────────────────
    private void OnAppNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node.Tag is not int pid) return;
        var proc = _lastCandidates.FirstOrDefault(p => p.Pid == pid);
        if (proc == null) return;
        if (Guard.Evaluate(proc) == RiskLevel.Forbidden)
        {
            MessageBox.Show(this, "该进程受保护，不可操作。", "拒绝",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"结束 {proc.ExeName}？\n该进程注册的热键会立即释放。\n启动信息会记录，可随时恢复。",
            "结束进程", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        var snapId = Guid.NewGuid().ToString("N");
        _store.Save(new DisabledSnapshot(
            snapId, DateTime.Now, false, pid, proc.ExeName, proc.FullPath,
            null, null, "user_disable"));

        if (_ctrl.GracefulClose(pid, TimeSpan.FromSeconds(3)))
        {
            e.Node.Text = $"{proc.ExeName}\t已结束\t双击可恢复";
            SetStatus($"已结束 {proc.ExeName}，其热键已释放");
        }
        else
        {
            SetStatus($"无法结束 {proc.ExeName}（可能受保护或需更高权限）");
        }
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
    }

    /// <summary>供离屏截图/自动化切换页签用。</summary>
    public void SelectTab(int index) => _tabBar.Select(index);
}
