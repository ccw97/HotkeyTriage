using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Interop;
using Xunit;

namespace HotkeyTriage.Tests;

public class MenuHotkeyReaderTests
{
    [Theory]
    [InlineData("新建(&N)\tCtrl+N", "新建", NativeMethods.MOD_CONTROL, 0x4E)]
    [InlineData("查找(&F)...\tCtrl+F", "查找", NativeMethods.MOD_CONTROL, 0x46)]
    [InlineData("另存为(&A)...\tCtrl+Shift+S", "另存为", NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, 0x53)]
    [InlineData("删除(&L)\tDel", "删除", 0u, 0x2E)]
    [InlineData("下一个(&N)\tF3", "下一个", 0u, 0x72)]
    public void Parses_menu_text_with_shortcut(string raw, string label, uint mods, uint vk)
    {
        var parsed = MenuHotkeyReader.ParseMenuItem(raw);
        Assert.NotNull(parsed);
        Assert.Equal(label, parsed!.Value.Label);
        Assert.Equal(mods, parsed.Value.Combo.Mods);
        Assert.Equal(vk, parsed.Value.Combo.Vk);
    }

    [Theory]
    [InlineData("仅文字没有快捷键")]
    [InlineData("")]
    [InlineData("\tCtrl+N")]              // 没有标签
    [InlineData("标签\t")]                 // 没有快捷键
    public void Rejects_menu_text_without_usable_shortcut(string raw)
    {
        Assert.Null(MenuHotkeyReader.ParseMenuItem(raw));
    }

    [Theory]
    [InlineData("Ctrl+N", NativeMethods.MOD_CONTROL, 0x4E)]
    [InlineData("Shift+F3", NativeMethods.MOD_SHIFT, 0x72)]
    [InlineData("Alt+Shift+Tab", NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, 0x09)]
    [InlineData("Ctrl+Alt+Del", NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, 0x2E)]
    [InlineData("Ctrl+Alt+Shift+F11", NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT, 0x7A)]
    public void Parses_shortcut_text(string text, uint mods, uint vk)
    {
        var parsed = MenuHotkeyReader.ParseShortcut(text);
        Assert.NotNull(parsed);
        Assert.Equal(mods, parsed!.Value.Mods);
        Assert.Equal(vk, parsed.Value.Vk);
    }

    [Fact]
    public void Ctrl_plus_parses_as_plus_key()
    {
        var parsed = MenuHotkeyReader.ParseShortcut("Ctrl++");
        Assert.NotNull(parsed);
        Assert.Equal(NativeMethods.MOD_CONTROL, parsed!.Value.Mods);
        Assert.Equal(0xBBu, parsed.Value.Vk);
    }

    [Theory]
    [InlineData("Hyper+N")]
    [InlineData("Ctrl+Foo")]
    [InlineData("")]
    public void Unknown_tokens_are_rejected(string text)
    {
        Assert.Null(MenuHotkeyReader.ParseShortcut(text));
    }

    [Fact]
    public void Missing_exe_yields_empty_map()
    {
        var map = MenuHotkeyReader.Read(@"Z:\definitely\not\here.exe");
        Assert.Empty(map);
    }
}
