using System.Drawing;
using System.Windows.Forms;
using HotkeyTriage.Core.Act;
using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Discover;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.App;

/// <summary>
/// 主窗口：双 Tab 树状视图 + 快扫/深扫 + 禁用/恢复。
/// </summary>
public sealed class MainForm : Form
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TreeView _sysTree = new() { Dock = DockStyle.Fill };
    private readonly TreeView _appTree = new() { Dock = DockStyle.Fill };
    private readonly Panel _topBar = new() { Dock = DockStyle.Top, Height = 40 };
    private readonly RoundButton _deepScanBtn = new() { Text = "深度扫描", Width = 100 };
    private readonly RoundButton _fastBtn = new() { Text = "快速识别", Width = 100 };
    private readonly Label _status = new() { Text = "就绪", Dock = DockStyle.Fill };

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
        Width = 900; Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(243, 243, 243);
        _topBar.BackColor = Color.FromArgb(32, 32, 32);
        _topBar.Height = 48;
        _topBar.Padding = new Padding(8, 8, 8, 8);
        _status.ForeColor = Color.FromArgb(220, 220, 220);
        _status.Font = new Font("Segoe UI", 9F);
        _sysTree.Font = new Font("Segoe UI", 9F);
        _appTree.Font = new Font("Segoe UI", 9F);
        _sysTree.BackColor = Color.White;
        _appTree.BackColor = Color.White;
        _sysTree.BorderStyle = BorderStyle.None;
        _appTree.BorderStyle = BorderStyle.None;
        _sysTree.ItemHeight = 24;
        _appTree.ItemHeight = 24;
        _deepScanBtn.BackColor = Color.FromArgb(0, 120, 215);

        _deepScanBtn.Click += OnDeepScan;
        _fastBtn.Click += OnFastIdentify;
        _fastBtn.FlatStyle = FlatStyle.Flat;
        _fastBtn.BackColor = Color.FromArgb(16, 124, 16);
        _fastBtn.ForeColor = Color.White;
        _topBar.Controls.Add(_status);
        _topBar.Controls.Add(_deepScanBtn);
        _topBar.Controls.Add(_fastBtn);
        _deepScanBtn.Dock = DockStyle.Left;
        _fastBtn.Dock = DockStyle.Left;

        var sysTab = new TabPage("Windows 自带") { Dock = DockStyle.Fill };
        sysTab.Controls.Add(_sysTree);
        var appTab = new TabPage("其他应用") { Dock = DockStyle.Fill };
        appTab.Controls.Add(_appTree);
        _tabs.TabPages.Add(sysTab);
        _tabs.TabPages.Add(appTab);

        Controls.Add(_tabs);
        Controls.Add(_topBar);

        _appTree.NodeMouseDoubleClick += OnAppNodeDoubleClick;

        Load += async (_, _) => { Log("Form Load 触发"); await QuickScanAsync(); };
    }

    private async Task QuickScanAsync()
    {
        Log("QuickScan 开始");
        _status.Text = "快扫中…";
        _deepScanBtn.Enabled = false;
        try
        {
            var scanner = new QuickScanner(_probe);
            _map = await Task.Run(() => scanner.Scan());
            Log($"QuickScan 完成: {_map.GridSize} 网格, {_map.Entries.Count} 命中");
            FillSystemTree();
            FillAppTreeQuick();
            var third = _map.Entries.Count(e => e.Kind == HotkeyKind.ThirdParty);
            _status.Text = $"扫描 {_map.GridSize} 组合，命中 {_map.Entries.Count}（第三方待归因 {third}）";
        }
        catch (Exception ex)
        {
            Log($"QuickScan 失败: {ex}");
            _status.Text = "快扫失败";
            MessageBox.Show(this,
                $"快扫失败：{ex.Message}\n\n详见日志：\n{Path.GetTempPath()}hotkey_triage.log",
                "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _deepScanBtn.Enabled = true;
        }
    }

    private void FillSystemTree()
    {
        _sysTree.Nodes.Clear();
        var groups = _map!.Entries
            .Where(e => e.Kind == HotkeyKind.System)
            .GroupBy(e => e.Group)
            .OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            var node = _sysTree.Nodes.Add($"{g.Key} ({g.Count()})");
            foreach (var e in g.OrderBy(x => x.Combo))
            {
                var text = $"{e.Combo,-16} {e.Description}";
                if (!string.IsNullOrEmpty(e.Hint)) text += $"  → {e.Hint}";
                node.Nodes.Add(text);
            }
        }
        _sysTree.ExpandAll();
    }

    private void FillAppTreeQuick()
    {
        _appTree.Nodes.Clear();
        var third = _map!.Entries.Where(e => e.Kind == HotkeyKind.ThirdParty).ToList();
        if (third.Count == 0)
        {
            _appTree.Nodes.Add("未检测到第三方占用");
            return;
        }
        _appTree.Nodes.Add($"检测到 {third.Count} 个热键被第三方占用");
        _appTree.Nodes.Add("点击顶部「深度扫描」确认归属（会临时关闭程序，自动恢复）");
        _appTree.Nodes.Add("");
        foreach (var e in third)
            _appTree.Nodes.Add(e.Combo);
    }

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
        _status.Text = "深扫中…";

        var deep = new DeepScanner(_probe, _ctrl, _store);
        var progress = new Progress<DeepScanProgress>(p =>
            _status.Text = $"深扫 {p.Processed}/{p.Total}  当前 {p.CurrentExe}  剩余 {p.RemainingKeys} 键");

        try
        {
            var result = await Task.Run(() => deep.Scan(approved, takenKeys, progress));
            FillAppTreeDeep(result);
            _status.Text = result.Completed
                ? $"深扫完成，归因 {result.AttributionByPid.Count} 进程"
                : $"深扫完成（部分），归因 {result.AttributionByPid.Count}，未归因 {result.Unattributed.Count}";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "深扫已中止";
        }
        finally
        {
            _deepScanBtn.Enabled = true;
        }
    }

    private void FillAppTreeDeep(DeepScanResult result)
    {
        _appTree.Nodes.Clear();
        foreach (var kv in result.AttributionByPid)
        {
            var proc = _lastCandidates.FirstOrDefault(p => p.Pid == kv.Key);
            var name = proc?.ExeName ?? $"PID {kv.Key}";
            var node = _appTree.Nodes.Add($"{name}  [PID {kv.Key}]");
            node.Tag = kv.Key;
            node.ToolTipText = proc?.FullPath ?? "";
            foreach (var key in kv.Value)
                node.Nodes.Add($"{KeyCombo.Format(key.Mods, key.Vk)}  →  {name}");
        }
        if (result.Unattributed.Count > 0)
        {
            var unk = _appTree.Nodes.Add($"无法归因 ({result.Unattributed.Count})");
            unk.Nodes.Add("占用者可能在未勾选的进程中（有窗口的默认未勾选）");
            unk.Nodes.Add("建议：重新深扫，勾选更多进程");
            foreach (var key in result.Unattributed)
                unk.Nodes.Add(KeyCombo.Format(key.Mods, key.Vk));
        }
        _appTree.ExpandAll();
    }

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

        var confirm = MessageBox.Show(this,
            "快速识别会模拟按下每个被占用的热键，触发占用者的功能。\n" +
            "（微信会弹截图框、录屏开始录、音乐切歌）\n\n" +
            "不会关闭任何程序。检测到反作弊进程会自动拒绝。\n\n继续？",
            "快速识别风险确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _fastBtn.Enabled = false;
        _deepScanBtn.Enabled = false;
        _status.Text = "快速识别中…";

        try
        {
            using var fast = new FastIdentify();
            var result = await Task.Run(() => fast.Identify(takenKeys, candidates));
            if (result.Refused)
            {
                MessageBox.Show(this, result.Reason, "快速识别被拒绝",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _status.Text = "快速识别被拒绝";
            }
            else
            {
                FillAppTreeFast(result);
                _status.Text = result.Completed
                    ? $"快速识别完成，归因 {result.AttributionByPid.Count} 进程"
                    : $"快速识别完成（部分），归因 {result.AttributionByPid.Count}，未归因 {result.Unattributed.Count}";
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
        _appTree.Nodes.Clear();
        foreach (var kv in result.AttributionByPid)
        {
            var proc = _lastCandidates.FirstOrDefault(p => p.Pid == kv.Key);
            var name = proc?.ExeName ?? $"PID {kv.Key}";
            var node = _appTree.Nodes.Add($"{name}  [PID {kv.Key}]");
            node.Tag = kv.Key;
            node.ToolTipText = proc?.FullPath ?? "";
            foreach (var key in kv.Value)
                node.Nodes.Add($"{KeyCombo.Format(key.Mods, key.Vk)}  →  {name}");
        }
        if (result.Unattributed.Count > 0)
        {
            var unk = _appTree.Nodes.Add($"无法归因 ({result.Unattributed.Count})");
            unk.Nodes.Add("可能是钩子型占用（AHK/录屏），或注入未覆盖的进程");
            foreach (var key in result.Unattributed)
                unk.Nodes.Add(KeyCombo.Format(key.Mods, key.Vk));
        }
        _appTree.ExpandAll();
    }

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
            $"结束 {proc.ExeName}？\n将记录启动信息，可一键恢复。",
            "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        var snapId = Guid.NewGuid().ToString("N");
        _store.Save(new DisabledSnapshot(
            snapId, DateTime.Now, false, pid, proc.ExeName, proc.FullPath,
            null, null, "user_disable"));
        _ctrl.GracefulClose(pid, TimeSpan.FromSeconds(3));
        e.Node.Text = $"{proc.ExeName}  [已结束]";
    }
}
