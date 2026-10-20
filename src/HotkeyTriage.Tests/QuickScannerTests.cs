using HotkeyTriage.Core.Discover;
using HotkeyTriage.Core.Probe;
using Xunit;

namespace HotkeyTriage.Tests;

public class QuickScannerTests
{
    private readonly QuickScanner _scanner = new(new HotkeyProbe());

    [Fact]
    public void Scan_returns_entries_for_occupied_combos()
    {
        var map = _scanner.Scan();
        // 本机几乎必然有被占用的热键（Win+D 等系统键）
        Assert.True(map.Entries.Count > 0, "未扫到任何被占用热键，环境异常");
    }

    [Fact]
    public void Scan_grid_size_is_828()
    {
        var map = _scanner.Scan();
        Assert.Equal(828, map.GridSize);
    }

    [Fact]
    public void Scan_completes_under_2_seconds()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _scanner.Scan();
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"快扫耗时 {sw.ElapsedMilliseconds}ms，超预算");
    }

    [Fact]
    public void Scan_classifies_system_entries()
    {
        var map = _scanner.Scan();
        Assert.Contains(map.Entries, e => e.Kind == HotkeyKind.System);
    }

    [Fact]
    public void Scan_system_entries_have_non_empty_group()
    {
        var map = _scanner.Scan();
        foreach (var e in map.Entries)
        {
            if (e.Kind == HotkeyKind.System)
                Assert.False(string.IsNullOrEmpty(e.Group));
        }
    }

    [Fact]
    public void Scan_third_party_entries_have_null_owner()
    {
        // 快扫阶段第三方条目不应有 owner（深扫才填）
        var map = _scanner.Scan();
        foreach (var e in map.Entries)
        {
            if (e.Kind == HotkeyKind.ThirdParty)
                Assert.Null(e.OwnerPid);
        }
    }

    [Fact]
    public void Scan_entries_have_combo_strings()
    {
        var map = _scanner.Scan();
        foreach (var e in map.Entries)
            Assert.False(string.IsNullOrEmpty(e.Combo));
    }
}
