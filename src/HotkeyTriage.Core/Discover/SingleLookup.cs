using HotkeyTriage.Core.Act;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Discover;

/// <summary>单键快查结论</summary>
public enum SingleKind
{
    /// <summary>系统层面无冲突（按键失灵原因在 IME/钩子/游戏内绑定）</summary>
    NoConflict,
    /// <summary>已定位到占用者</summary>
    Found,
    /// <summary>被占用但无法归因（候选都试过仍未释放）</summary>
    NotFound,
    /// <summary>探测失败，无法判定</summary>
    Unknown
}

public sealed record SingleLookupResult(SingleKind Kind, int? OwnerPid);

/// <summary>
/// 单键快查：用户按下一个键，快速回答"被谁占了"。
///
/// 流程：
///   Free   → 结论 C（无系统冲突）
///   Taken  → 调 DeepScanner 做定向遍历（只测这一个键，命中即停）
///
/// 比 DeepScan 全量快：只关心一个键，命中立刻返回。
/// </summary>
public sealed class SingleLookup
{
    private readonly IHotkeyProbe _probe;
    private readonly IProcessController _ctrl;
    private readonly DisabledListStore _store;

    public SingleLookup(IHotkeyProbe probe, IProcessController ctrl, DisabledListStore store)
    {
        _probe = probe;
        _ctrl = ctrl;
        _store = store;
    }

    public SingleLookupResult Lookup(uint mods, uint vk, IReadOnlyList<ProcInfo> candidates)
    {
        var r = _probe.Probe(mods, vk);
        if (r == ProbeResult.Free)
            return new SingleLookupResult(SingleKind.NoConflict, null);
        if (r == ProbeResult.Error)
            return new SingleLookupResult(SingleKind.Unknown, null);

        // Taken：定向遍历，DeepScanner 命中即停
        var deep = new DeepScanner(_probe, _ctrl, _store);
        var res = deep.Scan(candidates, new[] { (mods, vk) });
        if (res.AttributionByPid.Count > 0)
            return new SingleLookupResult(SingleKind.Found, res.AttributionByPid.First().Key);
        return new SingleLookupResult(SingleKind.NotFound, null);
    }
}
