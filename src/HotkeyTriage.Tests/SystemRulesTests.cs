using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Interop;
using Xunit;

namespace HotkeyTriage.Tests;

public class SystemRulesTests
{
    [Fact]
    public void Alt_Tab_is_reserved()
    {
        var info = SystemRules.Classify(NativeMethods.MOD_ALT, 0x09);
        Assert.Equal(SystemKind.Reserved, info.Kind);
        Assert.Equal("窗口管理", info.Group);
    }

    [Fact]
    public void Win_G_is_disableable_with_hint()
    {
        var info = SystemRules.Classify(NativeMethods.MOD_WIN, 0x47);
        Assert.Equal(SystemKind.Disableable, info.Kind);
        Assert.False(string.IsNullOrEmpty(info.Hint));
        Assert.Equal("截图录制", info.Group);
    }

    [Fact]
    public void Alt_A_is_not_system()
    {
        var info = SystemRules.Classify(NativeMethods.MOD_ALT, 0x41);
        Assert.Equal(SystemKind.NotSystem, info.Kind);
    }

    [Fact]
    public void Ctrl_Alt_Del_is_reserved()
    {
        var info = SystemRules.Classify(
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x2E);
        Assert.Equal(SystemKind.Reserved, info.Kind);
    }

    [Theory]
    [InlineData(0x44)] // D
    [InlineData(0x45)] // E
    [InlineData(0x4C)] // L
    [InlineData(0x52)] // R
    public void Win_common_keys_are_reserved(uint vk)
    {
        var info = SystemRules.Classify(NativeMethods.MOD_WIN, vk);
        Assert.Equal(SystemKind.Reserved, info.Kind);
    }

    [Fact]
    public void Win_unknown_combo_falls_back_to_reserved_shell()
    {
        // Win+Z（0x5A）不在表中，应兜底为系统壳热键
        var info = SystemRules.Classify(NativeMethods.MOD_WIN, 0x5A);
        Assert.Equal(SystemKind.Reserved, info.Kind);
    }

    [Fact]
    public void No_modifier_non_function_key_is_not_system()
    {
        // 裸 A 键不应被判为系统
        var info = SystemRules.Classify(0, 0x41);
        Assert.Equal(SystemKind.NotSystem, info.Kind);
    }

    [Theory]
    [InlineData(0u)]                                   // 裸 F12
    [InlineData(NativeMethods.MOD_SHIFT)]              // Shift+F12
    public void F12_is_reserved_for_debugger(uint mods)
    {
        // 实测：RegisterHotKey(F12) 与 RegisterHotKey(Shift+F12) 均返回 1409，
        // 但没有任何进程收到 WM_HOTKEY —— 系统永久保留给调试器造成的假阳性。
        // 必须归为系统保留，否则会永远挂在"第三方待归因"里查不到。
        var info = SystemRules.Classify(mods, 0x7B);
        Assert.Equal(SystemKind.Reserved, info.Kind);
        Assert.Equal("系统保留", info.Group);
    }

    [Theory]
    [InlineData(NativeMethods.MOD_CONTROL)]            // Ctrl+F12 实测 FREE
    [InlineData(NativeMethods.MOD_ALT)]                // Alt+F12 实测 FREE
    public void F12_with_ctrl_or_alt_is_not_reserved(uint mods)
    {
        var info = SystemRules.Classify(mods, 0x7B);
        Assert.Equal(SystemKind.NotSystem, info.Kind);
    }
}
