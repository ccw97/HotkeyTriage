using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Discover;

/// <summary>
/// 快扫：只做试注册探测，不打断任何程序。
///
/// 产出 HotkeyMap：
///   - 系统键（规则表命中）直接分类填充分组/说明/hint
///   - 第三方键标记 Kind=ThirdParty、Evidence=None、Owner=null（归属待深扫）
///
/// 耗时预算 < 1 秒（828 次试注册）。
/// </summary>
public sealed class QuickScanner
{
    private readonly HotkeyProbe _probe;

    public QuickScanner(HotkeyProbe probe)
    {
        _probe = probe;
    }

    public HotkeyMap Scan()
    {
        var grid = KeyGrid.All();
        var entries = new List<HotkeyEntry>(grid.Count / 6);

        foreach (var (mods, vk) in grid)
        {
            var result = _probe.Probe(mods, vk, out _);
            if (result != ProbeResult.Taken)
                continue;

            var combo = KeyCombo.Format(mods, vk);
            var sys = SystemRules.Classify(mods, vk);

            var kind = sys.Kind == SystemKind.NotSystem
                ? HotkeyKind.ThirdParty
                : HotkeyKind.System;
            var evidence = sys.Kind == SystemKind.NotSystem
                ? HotkeyEvidence.None
                : HotkeyEvidence.Rule;

            entries.Add(new HotkeyEntry(
                Mods: mods, Vk: vk, Combo: combo,
                Kind: kind, Evidence: evidence,
                Group: sys.Group, Description: sys.Description, Hint: sys.Hint,
                OwnerPid: null, OwnerExe: string.Empty, OwnerPath: string.Empty));
        }

        return new HotkeyMap
        {
            Mode = "quick",
            GridSize = grid.Count,
            Entries = entries
        };
    }
}
