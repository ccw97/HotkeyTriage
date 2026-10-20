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
}
