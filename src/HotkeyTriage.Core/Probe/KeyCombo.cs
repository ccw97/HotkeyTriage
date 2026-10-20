using HotkeyTriage.Core.Interop;

namespace HotkeyTriage.Core.Probe;

/// <summary>
/// 组合键格式化与名称解析。统一全项目的组合键字符串表示。
/// </summary>
public static class KeyCombo
{
    private static readonly (uint Mod, string Label)[] ModOrder =
    {
        (NativeMethods.MOD_CONTROL, "Ctrl"),
        (NativeMethods.MOD_ALT, "Alt"),
        (NativeMethods.MOD_SHIFT, "Shift"),
        (NativeMethods.MOD_WIN, "Win"),
    };

    private static readonly Dictionary<uint, string> VkNames = BuildVkNames();

    /// <summary>格式化为 "Ctrl+Alt+A" 形式</summary>
    public static string Format(uint mods, uint vk)
    {
        var parts = new List<string>(5);
        foreach (var (m, label) in ModOrder)
            if ((mods & m) != 0)
                parts.Add(label);
        parts.Add(VkName(vk));
        return string.Join("+", parts);
    }

    public static string VkName(uint vk) =>
        VkNames.TryGetValue(vk, out var n) ? n : $"0x{vk:X2}";

    private static Dictionary<uint, string> BuildVkNames()
    {
        var d = new Dictionary<uint, string>();
        for (uint c = 0x41; c <= 0x5A; c++) d[c] = ((char)c).ToString();
        for (uint c = 0x30; c <= 0x39; c++) d[c] = ((char)c).ToString();
        for (uint i = 0; i < 24; i++) d[0x70 + i] = $"F{i + 1}";
        d[0x09] = "Tab"; d[0x1B] = "Esc"; d[0x20] = "Space";
        d[0x21] = "PgUp"; d[0x22] = "PgDn"; d[0x23] = "End"; d[0x24] = "Home";
        d[0x2D] = "Insert"; d[0x2E] = "Delete";
        d[0xBA] = ";"; d[0xBB] = "="; d[0xBC] = ","; d[0xBD] = "-"; d[0xBE] = "."; d[0xBF] = "/";
        d[0xC0] = "`"; d[0xDB] = "["; d[0xDC] = "\\"; d[0xDD] = "]"; d[0xDE] = "'";
        return d;
    }
}
