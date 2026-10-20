using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Diag;

/// <summary>
/// 探针：对着真实运行的进程，把「界面右侧那一列」的原材料全部打印出来，
/// 用来判断某个程序不显示功能名/公司名时，问题出在数据层还是界面层。
/// </summary>
internal static class AppInfoLab
{
    public static int Run(string exeNameFragment)
    {
        var scanner = new ProcessScanner();
        var procs = scanner.Scan()
            .Where(p => p.ExeName.Contains(exeNameFragment, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine($"=== 匹配 \"{exeNameFragment}\" 的进程: {procs.Count} 个 ===");
        if (procs.Count == 0) return 1;

        foreach (var p in procs)
        {
            var info = AppInfoProvider.For(p);
            var menu = MenuHotkeyReader.Read(p.FullPath);

            Console.WriteLine();
            Console.WriteLine($"PID {p.Pid}   {p.ExeName}   有窗口={p.HasVisibleWindow}");
            Console.WriteLine($"  完整路径      : [{p.FullPath}]");
            Console.WriteLine($"  Company       : [{info.Company}]");
            Console.WriteLine($"  Product       : [{info.Product}]");
            Console.WriteLine($"  Vendor(用于显示): [{info.Vendor}]");
            Console.WriteLine($"  WindowTitle   : [{info.WindowTitle}]");
            Console.WriteLine($"  Identity      : [{info.Identity}]");
            Console.WriteLine($"  菜单功能条数   : {menu.Count}");
            foreach (var kv in menu.Take(5))
                Console.WriteLine($"      {KeyCombo.Format(kv.Key.Mods, kv.Key.Vk)} -> {kv.Value}");
        }
        return 0;
    }
}
