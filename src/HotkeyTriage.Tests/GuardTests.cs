using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Probe;
using Xunit;

namespace HotkeyTriage.Tests;

public class GuardTests
{
    private static ProcInfo Proc(string exe, bool hasWindow = false, int pid = 1) =>
        new(pid, exe, @"C:\foo\" + exe, hasWindow);

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("dwm.exe")]
    [InlineData("csrss.exe")]
    [InlineData("svchost.exe")]
    [InlineData("lsass.exe")]
    [InlineData("winlogon.exe")]
    public void System_critical_processes_are_forbidden(string exe)
    {
        Assert.Equal(RiskLevel.Forbidden, Guard.Evaluate(Proc(exe)));
    }

    [Theory]
    [InlineData("vgc.exe")]
    [InlineData("vgk.exe")]
    [InlineData("beservice.exe")]
    [InlineData("easyanticheat.exe")]
    [InlineData("mhyprot.exe")]
    [InlineData("sgguard.exe")]
    public void Anti_cheat_processes_are_forbidden(string exe)
    {
        Assert.Equal(RiskLevel.Forbidden, Guard.Evaluate(Proc(exe)));
    }

    [Fact]
    public void Normal_background_process_is_safe()
    {
        var p = Proc("weixin.exe", hasWindow: false);
        Assert.Equal(RiskLevel.Safe, Guard.Evaluate(p));
    }

    [Fact]
    public void Process_with_visible_window_is_caution()
    {
        var p = Proc("weixin.exe", hasWindow: true);
        Assert.Equal(RiskLevel.Caution, Guard.Evaluate(p));
    }

    [Fact]
    public void Forbidden_overrides_window_check()
    {
        // explorer 有窗口，但应 Forbidden 而非 Caution
        var p = Proc("explorer.exe", hasWindow: true);
        Assert.Equal(RiskLevel.Forbidden, Guard.Evaluate(p));
    }

    [Fact]
    public void Evaluation_is_case_insensitive()
    {
        Assert.Equal(RiskLevel.Forbidden, Guard.Evaluate(Proc("EXPLORER.EXE")));
        Assert.Equal(RiskLevel.Forbidden, Guard.Evaluate(Proc("VgC.ExE")));
    }
}
