using HotkeyTriage.Core.Discover;
using HotkeyTriage.Core.Probe;
using Xunit;

namespace HotkeyTriage.Tests;

/// <summary>
/// 演示用：跑一次真实快扫，把结果写到临时文件供肉眼检查。
/// 不是契约测试，是 M2 里程碑的可视化验证。
/// </summary>
public class QuickScannerDemo
{
    [Fact]
    public void Write_real_scan_result_to_temp()
    {
        var scanner = new QuickScanner(new HotkeyProbe());
        var map = scanner.Scan();

        var sys = map.Entries.Where(e => e.Kind == HotkeyKind.System).ToList();
        var third = map.Entries.Where(e => e.Kind == HotkeyKind.ThirdParty).ToList();
        var disableable = sys.Where(e => !string.IsNullOrEmpty(e.Hint)).ToList();
        var reserved = sys.Where(e => string.IsNullOrEmpty(e.Hint)).ToList();

        var path = Path.Combine(Path.GetTempPath(), "hotkey_scan_demo.txt");
        using var w = new StreamWriter(path, false, System.Text.Encoding.UTF8);

        w.WriteLine("HotkeyTriage 快扫真实输出（M2 演示）");
        w.WriteLine($"扫描 {map.GridSize} 个组合 → 命中 {map.Entries.Count} 个");
        w.WriteLine($"  系统保留 {reserved.Count} / 系统可关 {disableable.Count} / 第三方待归因 {third.Count}");
        w.WriteLine();

        w.WriteLine("═══ 系统功能（可关闭）═══");
        foreach (var e in disableable)
            w.WriteLine($"  {e.Combo,-16} {e.Description,-24} → {e.Hint}");
        w.WriteLine();

        w.WriteLine("═══ 系统保留（不可关闭，前 15 条样例）═══");
        foreach (var e in reserved.Take(15))
            w.WriteLine($"  {e.Combo,-16} {e.Description}");
        if (reserved.Count > 15)
            w.WriteLine($"  ...（其余 {reserved.Count - 15} 条略）");
        w.WriteLine();

        w.WriteLine("═══ 第三方待归因（深扫可定位归属）═══");
        foreach (var e in third)
            w.WriteLine($"  {e.Combo}");
        w.WriteLine();
        w.WriteLine("说明：第三方条目的 owner 在快扫阶段为空，需深扫遍历进程才能归因。");

        Assert.True(map.Entries.Count > 0);
    }
}
