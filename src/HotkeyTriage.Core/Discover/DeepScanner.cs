using HotkeyTriage.Core.Act;
using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Discover;

/// <summary>
/// 深扫：遍历候选进程，逐个结束复测，建立「进程 → 热键」归属映射。
///
/// 算法（设计文档 §3.3）：
///   1. 候选按权重排序（有窗口+30，非系统目录+10）
///   2. 逐个：落盘快照 → 关闭 → 等 10ms（H3 实测） → 复测所有未归因键 → 记录释放 → 恢复
///   3. 未归因键归零则提前结束
///
/// 安全约束：
///   - Guard.Forbidden 进程已在候选构造时排除（调用方负责）
///   - 自身进程排除
///   - 每次关闭前必须落盘快照（DisabledListStore）
///   - 立即恢复，不等扫描结束
/// </summary>
public sealed class DeepScanner
{
    private readonly IHotkeyProbe _probe;
    private readonly IProcessController _ctrl;
    private readonly DisabledListStore _store;

    private static readonly string SystemRoot =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public DeepScanner(IHotkeyProbe probe, IProcessController ctrl, DisabledListStore store)
    {
        _probe = probe;
        _ctrl = ctrl;
        _store = store;
    }

    public DeepScanResult Scan(
        IReadOnlyList<ProcInfo> candidates,
        IReadOnlyList<(uint Mods, uint Vk)> takenKeys,
        IProgress<DeepScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var remaining = takenKeys.ToList();
        var attribution = new Dictionary<int, List<(uint Mods, uint Vk)>>();
        var ordered = candidates
            .Where(p => p.Pid != Environment.ProcessId && p.Pid > 4)
            .OrderByDescending(Score)
            .ToList();
        int total = ordered.Count;
        int processed = 0;

        foreach (var proc in ordered)
        {
            if (remaining.Count == 0) break;
            ct.ThrowIfCancellationRequested();
            processed++;
            progress?.Report(new DeepScanProgress(processed, total, proc.ExeName, remaining.Count));

            // 关闭前必须落盘快照
            var snapId = Guid.NewGuid().ToString("N");
            _store.Save(new DisabledSnapshot(
                snapId, DateTime.Now, false,
                proc.Pid, proc.ExeName, proc.FullPath,
                null, null, "deep_scan"));

            try
            {
                if (!_ctrl.GracefulClose(proc.Pid, TimeSpan.FromSeconds(3)))
                    _ctrl.ForceKill(proc.Pid);
            }
            catch
            {
                _store.MarkRestored(snapId);
                continue;
            }

            Thread.Sleep(10); // H3 实测：释放即时，10ms 为工程裕度

            // 复测所有未归因键
            var stillTaken = new List<(uint Mods, uint Vk)>();
            foreach (var key in remaining)
            {
                if (_probe.Probe(key.Mods, key.Vk) == ProbeResult.Taken)
                    stillTaken.Add(key);
            }

            if (stillTaken.Count < remaining.Count)
            {
                // 有键释放 → 归属到本进程
                var released = remaining.Except(stillTaken).ToList();
                attribution[proc.Pid] = released;
                remaining = stillTaken;
            }

            // 立即恢复；失败则保留快照为未恢复（启动时会提示用户手动恢复）
            var restored = !string.IsNullOrEmpty(proc.FullPath)
                && _ctrl.Restore(proc.FullPath) != null;
            if (restored)
                _store.MarkRestored(snapId);
        }

        return new DeepScanResult
        {
            AttributionByPid = attribution,
            Unattributed = remaining,
            Completed = remaining.Count == 0,
            ScannedCount = processed
        };
    }

    private static int Score(ProcInfo p)
    {
        int s = 0;
        if (p.HasVisibleWindow) s += 30;
        if (!string.IsNullOrEmpty(p.FullPath)
            && !p.FullPath.StartsWith(SystemRoot, StringComparison.OrdinalIgnoreCase))
            s += 10;
        return s;
    }
}
