using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HotkeyTriage.Core.Interop;

namespace HotkeyTriage.Core.Attribute;

/// <summary>
/// 从程序自身的菜单资源里读出「快捷键 → 功能名」。
///
/// 为什么是它：Windows 不提供任何"热键反查功能名"的 API。但传统 Win32 程序的菜单项
/// 会把加速键写进文本，形如 <c>查找(&amp;F)...\tCtrl+F</c>。把这些读出来，就能把
/// <c>Alt+A</c> 变成 <c>截图</c> —— 而且数据来自程序本体，不会像知识库那样过期。
///
/// 边界（必须诚实）：
///   - 自绘 UI 的程序（微信、钉钉、Electron 应用）没有菜单资源，读不到，返回空表。
///   - 只在内存映射资源，用 LOAD_LIBRARY_AS_DATAFILE，不加载不执行目标代码。
///   - 任何异常都吞掉并返回空表：这是"锦上添花"的能力，不允许影响主流程。
/// </summary>
public static class MenuHotkeyReader
{
    private const int MaxMenuResources = 8;
    private const int MaxItems = 800;

    private static readonly IReadOnlyDictionary<(uint Mods, uint Vk), string> Empty
        = new Dictionary<(uint, uint), string>();

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<(uint, uint), string>> Cache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>读取（带缓存）。路径不存在或解析失败时返回空表。</summary>
    public static IReadOnlyDictionary<(uint Mods, uint Vk), string> Read(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return Empty;
        return Cache.GetOrAdd(exePath, ReadCore);
    }

    private static IReadOnlyDictionary<(uint, uint), string> ReadCore(string exePath)
    {
        var result = new Dictionary<(uint, uint), string>();
        if (!File.Exists(exePath)) return Empty;

        IntPtr hMod = IntPtr.Zero;
        try
        {
            hMod = NativeMethods.LoadLibraryExW(exePath, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_AS_DATAFILE);
            if (hMod == IntPtr.Zero) return Empty;

            var names = new List<IntPtr>();
            NativeMethods.EnumResourceNamesW(hMod, NativeMethods.RT_MENU,
                (m, t, n, l) => { names.Add(n); return true; }, IntPtr.Zero);

            var items = new List<string>();
            foreach (var name in names.Take(MaxMenuResources))
            {
                var menu = NativeMethods.LoadMenuW(hMod, name);
                if (menu == IntPtr.Zero) continue;
                try { Walk(menu, items); }
                finally { NativeMethods.DestroyMenu(menu); }
                if (items.Count >= MaxItems) break;
            }

            foreach (var item in items)
            {
                var parsed = ParseMenuItem(item);
                if (parsed == null) continue;
                var (combo, label) = parsed.Value;
                if (!result.ContainsKey(combo)) result[combo] = label;
            }
            return result.Count == 0 ? Empty : result;
        }
        catch
        {
            return Empty;
        }
        finally
        {
            if (hMod != IntPtr.Zero) NativeMethods.FreeLibrary(hMod);
        }
    }

    private static void Walk(IntPtr menu, List<string> outList, int depth = 0)
    {
        if (menu == IntPtr.Zero || depth > 3 || outList.Count >= MaxItems) return;
        int n = NativeMethods.GetMenuItemCount(menu);
        for (int i = 0; i < n && outList.Count < MaxItems; i++)
        {
            var sb = new StringBuilder(512);
            NativeMethods.GetMenuStringW(menu, (uint)i, sb, sb.Capacity, NativeMethods.MF_BYPOSITION);
            var text = sb.ToString();
            if (!string.IsNullOrWhiteSpace(text)) outList.Add(text);

            var sub = NativeMethods.GetSubMenu(menu, i);
            if (sub != IntPtr.Zero) Walk(sub, outList, depth + 1);
        }
    }

    /// <summary>
    /// 解析一条菜单文本：<c>"查找(&amp;F)...\tCtrl+F"</c> → ((Ctrl, F), "查找")。
    /// 没有快捷键标注或解析不出的返回 null。
    /// </summary>
    public static ((uint Mods, uint Vk) Combo, string Label)? ParseMenuItem(string menuText)
    {
        if (string.IsNullOrWhiteSpace(menuText)) return null;

        int tab = menuText.LastIndexOf('\t');
        if (tab <= 0 || tab == menuText.Length - 1) return null;

        string label = CleanLabel(menuText[..tab]);
        string shortcut = menuText[(tab + 1)..].Trim();
        if (label.Length == 0 || shortcut.Length == 0) return null;

        var combo = ParseShortcut(shortcut);
        return combo == null ? null : (combo.Value, label);
    }

    /// <summary>去掉加速键标记：<c>删除(&amp;L)</c> → <c>删除</c>，<c>查找(&amp;F)...</c> → <c>查找</c>。</summary>
    private static string CleanLabel(string raw) =>
        AccelMark.Replace(raw, "")
                 .Replace("&", "")
                 .Replace("...", "")
                 .Replace("…", "")
                 .Trim();

    private static readonly System.Text.RegularExpressions.Regex AccelMark =
        new(@"\(&?\w\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>把 "Ctrl+Shift+N" / "Del" / "F3" 解析成 (mods, vk)。</summary>
    public static (uint Mods, uint Vk)? ParseShortcut(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parts = text.Split('+', StringSplitOptions.None);
        uint mods = 0;
        string keyPart;

        // 形如 "Ctrl++"：最后一个 '+' 是按键本身
        if (parts.Length >= 2 && parts[^1].Length == 0)
        {
            keyPart = "+";
            for (int i = 0; i < parts.Length - 2; i++)
                if (ApplyModifier(parts[i], ref mods) == false) return null;
        }
        else
        {
            keyPart = parts[^1].Trim();
            for (int i = 0; i < parts.Length - 1; i++)
                if (ApplyModifier(parts[i], ref mods) == false) return null;
        }

        var vk = KeyNameToVk(keyPart);
        return vk == null ? null : (mods, vk.Value);
    }

    private static bool ApplyModifier(string token, ref uint mods)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "ctrl": case "control": mods |= NativeMethods.MOD_CONTROL; return true;
            case "alt": mods |= NativeMethods.MOD_ALT; return true;
            case "shift": mods |= NativeMethods.MOD_SHIFT; return true;
            case "win": case "windows": mods |= NativeMethods.MOD_WIN; return true;
            default: return false;
        }
    }

    private static uint? KeyNameToVk(string key)
    {
        if (key.Length == 0) return null;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
            return c switch
            {
                '+' => 0xBB, '-' => 0xBD, '=' => 0xBB, ',' => 0xBC,
                '.' => 0xBE, '/' => 0xBF, ';' => 0xBA, '`' => 0xC0,
                '[' => 0xDB, ']' => 0xDD, '\\' => 0xDC, '\'' => 0xDE,
                _ => null,
            };
        }

        var k = key.ToUpperInvariant();
        if (k.Length >= 2 && k[0] == 'F' && int.TryParse(k[1..], out int fn) && fn >= 1 && fn <= 24)
            return (uint)(0x70 + fn - 1);

        return k switch
        {
            "DEL" or "DELETE" => 0x2E,
            "INS" or "INSERT" => 0x2D,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" or "SPACEBAR" => 0x20,
            "TAB" => 0x09,
            "ENTER" or "RETURN" => 0x0D,
            "BACKSPACE" or "BACK" or "BS" => 0x08,
            "HOME" => 0x24,
            "END" => 0x23,
            "PGUP" or "PAGEUP" => 0x21,
            "PGDN" or "PAGEDOWN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            _ => null,
        };
    }
}
