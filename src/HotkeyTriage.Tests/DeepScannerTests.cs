using HotkeyTriage.Core.Act;
using HotkeyTriage.Core.Discover;
using HotkeyTriage.Core.Probe;
using System.Diagnostics;
using Xunit;

namespace HotkeyTriage.Tests;

/// <summary>
/// DeepScanner 确定性测试：用 Fake 模拟"某进程关闭后某键释放"，
/// 验证归因逻辑不依赖真实进程。
/// </summary>
public class DeepScannerTests
{
    private const uint MOD = 0x0002 | 0x0001; // Ctrl+Alt

    private static ProcInfo Proc(int pid, string exe, bool win = false) =>
        new(pid, exe, $@"F:\fake\{exe}", win);

    /// <summary>
    /// 场景：两个进程各注册一个键。关闭 A 后 VK_A 释放、VK_B 仍占；关闭 B 后 VK_B 释放。
    /// 期望：归属到各自 PID，无未归因。
    /// </summary>
    [Fact]
    public void Attributes_keys_to_correct_processes()
    {
        var pidA = 1001;
        var pidB = 1002;
        var keyA = (MOD, 0x78u); // F9
        var keyB = (MOD, 0x79u); // F10
        var closed = new HashSet<int>();

        var owner = new Dictionary<(uint, uint), int>
        {
            [keyA] = pidA,
            [keyB] = pidB,
        };

        var fakeProbe = new FakeProbe(owner, closed);
        var fakeCtrl = new FakeController(closed);
        var store = new DisabledListStore(Path.Combine(Path.GetTempPath(), "hk_test_" + Guid.NewGuid().ToString("N")[..8]));

        var scanner = new DeepScanner(fakeProbe, fakeCtrl, store);
        var candidates = new List<ProcInfo> { Proc(pidA, "a.exe"), Proc(pidB, "b.exe") };

        var result = scanner.Scan(candidates, new[] { keyA, keyB });

        Assert.True(result.Completed);
        Assert.Empty(result.Unattributed);
        Assert.Contains(pidA, result.AttributionByPid.Keys);
        Assert.Contains(keyA, result.AttributionByPid[pidA]);
        Assert.Contains(pidB, result.AttributionByPid.Keys);
        Assert.Contains(keyB, result.AttributionByPid[pidB]);
    }

    /// <summary>场景：某键的占用者不在候选中 → 该键应留在未归因</summary>
    [Fact]
    public void Leaves_unattributed_when_owner_not_in_candidates()
    {
        var key = (MOD, 0x7Au); // F11
        var closed = new HashSet<int>();
        var owner = new Dictionary<(uint, uint), int> { [key] = 9999 }; // 不在候选

        var fakeProbe = new FakeProbe(owner, closed);
        var fakeCtrl = new FakeController(closed);
        var store = new DisabledListStore(Path.Combine(Path.GetTempPath(), "hk_test_" + Guid.NewGuid().ToString("N")[..8]));

        var scanner = new DeepScanner(fakeProbe, fakeCtrl, store);
        var result = scanner.Scan(new[] { Proc(100, "foo.exe") }, new[] { key });

        Assert.False(result.Completed);
        Assert.Contains(key, result.Unattributed);
    }

    /// <summary>场景：所有键在第一个候选就全部归因 → 应提前结束，不碰其余候选</summary>
    [Fact]
    public void Stops_early_when_all_keys_attributed()
    {
        var key = (MOD, 0x7Bu); // F12
        var closed = new HashSet<int>();
        var owner = new Dictionary<(uint, uint), int> { [key] = 100 };

        var fakeProbe = new FakeProbe(owner, closed);
        var fakeCtrl = new FakeController(closed);
        var store = new DisabledListStore(Path.Combine(Path.GetTempPath(), "hk_test_" + Guid.NewGuid().ToString("N")[..8]));

        var scanner = new DeepScanner(fakeProbe, fakeCtrl, store);
        var candidates = new List<ProcInfo>
        {
            Proc(100, "first.exe"),
            Proc(101, "second.exe"),
            Proc(102, "third.exe"),
        };

        var result = scanner.Scan(candidates, new[] { key });

        Assert.True(result.Completed);
        Assert.Equal(1, result.ScannedCount); // 只扫了第一个
        Assert.DoesNotContain(101, closed);   // 没碰第二个
        Assert.DoesNotContain(102, closed);
    }

    // ---- Fake 实现 ----

    private sealed class FakeProbe : IHotkeyProbe
    {
        private readonly Dictionary<(uint, uint), int> _owner;
        private readonly HashSet<int> _closed;

        public FakeProbe(Dictionary<(uint, uint), int> owner, HashSet<int> closed)
        {
            _owner = owner;
            _closed = closed;
        }

        public ProbeResult Probe(uint mods, uint vk)
        {
            if (!_owner.TryGetValue((mods, vk), out var pid))
                return ProbeResult.Free;
            return _closed.Contains(pid) ? ProbeResult.Free : ProbeResult.Taken;
        }
    }

    private sealed class FakeController : IProcessController
    {
        private readonly HashSet<int> _closed;

        public FakeController(HashSet<int> closed) { _closed = closed; }

        public bool GracefulClose(int pid, TimeSpan timeout)
        {
            _closed.Add(pid);
            return true;
        }

        public bool ForceKill(int pid)
        {
            _closed.Add(pid);
            return true;
        }

        public Process? Restore(string path, string? args = null, string? workingDir = null) => null;
    }
}
