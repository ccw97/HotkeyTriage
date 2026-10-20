using System.Drawing;
using System.Windows.Forms;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.App;

/// <summary>
/// 深扫确认对话框：列出将被关闭的候选进程，用户勾选确认。
/// Forbidden 进程不在列表中（调用方已过滤）。
/// 有窗口的默认不勾选（可能有未保存内容）。
/// </summary>
public sealed class DeepScanDialog : Form
{
    private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly Button _ok = new() { Text = "开始扫描", Width = 120 };
    private readonly Button _cancel = new() { Text = "取消", Width = 80 };
    private readonly Label _hint = new() { Dock = DockStyle.Top, Height = 70 };

    private readonly List<ProcInfo> _all;

    public IReadOnlyList<ProcInfo> ApprovedCandidates { get; private set; }
        = Array.Empty<ProcInfo>();

    public DeepScanDialog(IReadOnlyList<ProcInfo> candidates)
    {
        _all = candidates.ToList();
        Text = "深度扫描确认";
        Width = 620; Height = 520;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        _hint.Text = "[!] 将临时关闭勾选的程序，扫描完成后自动恢复。\r\n" +
                     "有窗口的默认不勾选（关闭会中断你正在做的事，请谨慎勾选）。\r\n" +
                     "explorer.exe 与反作弊进程不会出现在列表中。";
        _hint.ForeColor = Color.FromArgb(180, 60, 40);

        var btnBar = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        _ok.Dock = DockStyle.Left;
        _cancel.Dock = DockStyle.Left;
        btnBar.Controls.Add(_cancel);
        btnBar.Controls.Add(_ok);

        foreach (var p in candidates)
        {
            var label = $"{p.ExeName,-26} PID {p.Pid,-6} {(p.HasVisibleWindow ? "（有窗口）" : "")}";
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

        Controls.Add(_list);
        Controls.Add(_hint);
        Controls.Add(btnBar);
    }
}
