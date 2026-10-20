using HotkeyTriage.Core.Interop;

namespace HotkeyTriage.Core.Probe;

/// <summary>
/// 候选热键网格：13 组修饰键 × 63 个主键 = 828 个组合。
/// 覆盖游戏与日常软件最易冲突的区域。裸字母/数字键不属于全局热键范畴，仅 F1–F12 允许无修饰。
/// </summary>
public static class KeyGrid
{
    private static readonly uint[] ModifierSets =
    {
        0,
        NativeMethods.MOD_CONTROL, NativeMethods.MOD_ALT,
        NativeMethods.MOD_SHIFT, NativeMethods.MOD_WIN,
        NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
        NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
        NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT,
        NativeMethods.MOD_CONTROL | NativeMethods.MOD_WIN,
        NativeMethods.MOD_ALT | NativeMethods.MOD_WIN,
        NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN,
        NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT,
        NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_WIN,
    };

    private static readonly uint[] MainKeys = BuildMainKeys();

    /// <summary>生成全部候选组合</summary>
    public static IReadOnlyList<(uint Mods, uint Vk)> All()
    {
        var list = new List<(uint, uint)>(828);
        foreach (var mods in ModifierSets)
            foreach (var vk in MainKeys)
            {
                // 裸字母/数字不属于全局热键范畴，仅功能键允许
                if (mods == 0 && (vk < 0x70 || vk > 0x7B))
                    continue;
                list.Add((mods, vk));
            }
        return list;
    }

    private static uint[] BuildMainKeys()
    {
        var keys = new List<uint>(63);
        for (uint c = 0x41; c <= 0x5A; c++) keys.Add(c); // A-Z
        for (uint c = 0x30; c <= 0x39; c++) keys.Add(c); // 0-9
        for (uint i = 0; i < 12; i++) keys.Add((uint)(0x70 + i)); // F1-F12
        keys.AddRange(new uint[]
        {
            0x09, 0x1B, 0x20, 0x21, 0x22, 0x23, 0x24, 0x2D, 0x2E,
            0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE
        });
        return keys.ToArray();
    }
}
