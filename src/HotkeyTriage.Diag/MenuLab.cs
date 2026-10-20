using System.Runtime.InteropServices;
using System.Text;

namespace HotkeyTriage.Diag;

/// <summary>
/// 探路脚本（一次性）：验证能否从 exe 的菜单资源里读出「快捷键 → 功能名」。
/// 关键点：用 LOAD_LIBRARY_AS_DATAFILE 只加载资源、不执行代码，因此对任何程序都安全。
/// </summary>
internal static class MenuLab
{
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    private const uint MF_BYPOSITION = 0x400;
    private static readonly IntPtr RT_MENU = 4;

    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpType, IntPtr lpName, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr hFile, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNamesW(IntPtr hModule, IntPtr lpType, EnumResNameProc cb, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadMenuW(IntPtr hModule, IntPtr lpName);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(IntPtr hMenu, uint uIdItem, StringBuilder lpString, int nMax, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSubMenu(IntPtr hMenu, int pos);

    public static int Run(string exePath)
    {
        Console.WriteLine($"=== {exePath} ===");
        IntPtr h = LoadLibraryExW(exePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (h == IntPtr.Zero)
        {
            Console.WriteLine($"  加载失败 err={Marshal.GetLastWin32Error()}");
            return 1;
        }

        try
        {
            var names = new List<IntPtr>();
            EnumResourceNamesW(h, RT_MENU, (m, t, n, l) => { names.Add(n); return true; }, IntPtr.Zero);
            Console.WriteLine($"  菜单资源数: {names.Count}");

            var items = new List<string>();
            foreach (var n in names.Take(8))
            {
                var menu = LoadMenuW(h, n);
                if (menu == IntPtr.Zero) continue;
                Walk(menu, 1, items, 400);
                if (items.Count > 150) break;
            }

            Console.WriteLine($"  菜单项条数: {items.Count}");
            var hits = items.Where(t => t.Contains('\t')).ToList();
            Console.WriteLine($"  带快捷键标注的项: {hits.Count}");
            foreach (var t in hits.Take(15))
                Console.WriteLine("     " + t.Trim());
            if (hits.Count == 0)
                foreach (var t in items.Take(8))
                    Console.WriteLine("     样本: " + t.Trim());
            return 0;
        }
        finally { FreeLibrary(h); }
    }

    private static void Walk(IntPtr menu, int depth, List<string> outList, int limit)
    {
        if (menu == IntPtr.Zero || outList.Count >= limit) return;
        int n = GetMenuItemCount(menu);
        for (int i = 0; i < n && outList.Count < limit; i++)
        {
            var sb = new StringBuilder(512);
            GetMenuStringW(menu, (uint)i, sb, sb.Capacity, MF_BYPOSITION);
            var text = sb.ToString();
            if (text.Length > 0) outList.Add(new string(' ', depth * 2) + text);

            var sub = GetSubMenu(menu, i);
            if (sub != IntPtr.Zero) Walk(sub, depth + 1, outList, limit);
        }
    }
}
