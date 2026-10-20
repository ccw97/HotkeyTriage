using HotkeyTriage.Core.Act;
using System.Diagnostics;
using Xunit;

namespace HotkeyTriage.Tests;

/// <summary>
/// ProcessController 集成测试。
///
/// 教训（2026-10-20）：这些用例最初用 <c>Process.Start("notepad.exe")</c> 验证，
/// 结果每跑一次测试就弹一个记事本窗口；更要命的是 Win11 的 notepad.exe 只是
/// 「应用执行别名」，真正拉起的是 WindowsApps 里的商店版，
/// 用 UseShellExecute=true 启动后拿到的 Process 是转发进程，Kill() 杀不掉真实窗口，
/// 于是窗口会一直留在桌面上。
///
/// 现在的原则：测试**不允许产生任何可见窗口**。
///   - 用 cmd + ping 这类控制台进程（CreateNoWindow=true）做结束/强杀目标
///   - 启动类行为（Restore）不再自动化，改为人工用 Diag 验证
/// </summary>
public class ProcessControllerTests
{
    private readonly ProcessController _ctrl = new();

    /// <summary>起一个无窗口、长驻的控制台进程作为靶子。</summary>
    private static Process StartSilentTarget()
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 120 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        var p = Process.Start(psi);
        Assert.NotNull(p);
        Thread.Sleep(250);
        return p!;
    }

    private static void KillQuietly(Process? p)
    {
        try
        {
            if (p != null && !p.HasExited) { p.Kill(); p.WaitForExit(2000); }
        }
        catch { /* 清理失败不掩盖真正的断言失败 */ }
    }

    [Fact]
    public void ForceKill_terminates_target_process()
    {
        var p = StartSilentTarget();
        try
        {
            Assert.False(p.HasExited);
            var ok = _ctrl.ForceKill(p.Id);
            Assert.True(ok);
            Assert.True(p.WaitForExit(2000));
        }
        finally { KillQuietly(p); }
    }

    [Fact]
    public void ForceKill_returns_false_for_nonexistent_pid()
    {
        Assert.False(_ctrl.ForceKill(999999));
    }

    [Fact]
    public void GracefulClose_returns_false_for_nonexistent_pid()
    {
        Assert.False(_ctrl.GracefulClose(999999, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Restore_returns_null_for_missing_path()
    {
        Assert.Null(_ctrl.Restore(@"Z:\definitely\not\here.exe"));
    }

    // 真实窗口的「优雅关闭」与「重启」需要交互式桌面与可见窗口，
    // 自动化会产生用户可见的副作用（弹窗/窗口残留），改为人工用
    // HotkeyTriage.Diag 验证，不在测试套件里跑。
    [Fact(Skip = "需可见窗口，会产生用户可见副作用；改由 Diag 人工验证")]
    public void GracefulClose_closes_visible_window() { }

    [Fact(Skip = "需启动真实程序，Win11 商店应用的 Process 句柄不代表窗口进程；改由 Diag 人工验证")]
    public void Restore_relaunches_visible_app() { }
}
