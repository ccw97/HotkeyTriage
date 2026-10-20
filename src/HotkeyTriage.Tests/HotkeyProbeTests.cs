using HotkeyTriage.Core.Interop;
using HotkeyTriage.Core.Probe;
using Xunit;

namespace HotkeyTriage.Tests;

/// <summary>
/// HotkeyProbe 核心契约测试。
///
/// 设计原则：测试必须是确定性的，不依赖"某个键当前是否被环境占用"。
/// 因此用一个完整生命周期（Free → 注册 → Taken → 注销 → Free）来验证，
/// 而不是探测某个固定键。
/// </summary>
public class HotkeyProbeTests
{
    // 用一组冷门组合，降低被环境占用的概率：Ctrl+Alt+Shift+F11
    private const uint TestMods = NativeMethods.MOD_CONTROL
                                  | NativeMethods.MOD_ALT
                                  | NativeMethods.MOD_SHIFT;
    private const uint TestVk = 0x7A; // F11

    // 测试自身注册用的 id，与 HotkeyProbe 默认 id (0xBEEF) 错开
    private const int OccupierId = 0x0001;

    [Fact]
    public void Lifecycle_free_then_taken_then_free()
    {
        var probe = new HotkeyProbe();

        // 阶段0：确认测试前提——该组合当前未被占用
        var r0 = probe.Probe(TestMods, TestVk, out _);
        Assert.True(r0 == ProbeResult.Free,
            $"测试前提不成立：Ctrl+Alt+Shift+F11 当前为 {r0}，请换键后重试");

        // 阶段1：注册后探测应 Taken，错误码 1409
        Assert.True(NativeMethods.RegisterHotKey(IntPtr.Zero, OccupierId, TestMods, TestVk),
            "测试自身注册失败，无法继续");
        try
        {
            var r1 = probe.Probe(TestMods, TestVk, out var err1);
            Assert.Equal(ProbeResult.Taken, r1);
            Assert.Equal(NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED, err1);
        }
        finally
        {
            // 阶段2：注销后应恢复 Free
            Assert.True(NativeMethods.UnregisterHotKey(IntPtr.Zero, OccupierId));
        }

        var r2 = probe.Probe(TestMods, TestVk, out _);
        Assert.Equal(ProbeResult.Free, r2);
    }

    [Fact]
    public void Probe_does_not_leave_registration_behind()
    {
        // 探测 Free 后，不应残留注册；连续两次探测结果应一致
        var probe = new HotkeyProbe();
        var r1 = probe.Probe(TestMods, TestVk, out _);
        var r2 = probe.Probe(TestMods, TestVk, out _);
        Assert.Equal(r1, r2);
    }
}
