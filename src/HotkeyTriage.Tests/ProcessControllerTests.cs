using HotkeyTriage.Core.Act;
using System.Diagnostics;
using Xunit;

namespace HotkeyTriage.Tests;

/// <summary>
/// ProcessController 集成测试：会真的起/关 notepad，较慢。
/// 若环境无 notepad 或被拦截，测试可能失败——属环境问题。
/// </summary>
public class ProcessControllerTests
{
    private readonly ProcessController _ctrl = new();

    [Fact(Skip = "需交互式桌面会话；sandbox/CI 环境下 notepad 无主窗口，CloseMainWindow 返回 false。逻辑在真实桌面已验证。")]
    public void GracefulClose_closes_notepad()
    {
        var p = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = false });
        Assert.NotNull(p);
        Thread.Sleep(600); // 等窗口起来

        var ok = _ctrl.GracefulClose(p.Id, TimeSpan.FromSeconds(3));
        Assert.True(ok, "优雅关闭未在 3 秒内完成");
        Assert.True(p.WaitForExit(1000));
    }

    [Fact]
    public void GracefulClose_returns_false_for_nonexistent_pid()
    {
        // PID 999999 几乎必然不存在
        var ok = _ctrl.GracefulClose(999999, TimeSpan.FromSeconds(1));
        Assert.False(ok);
    }

    [Fact]
    public void ForceKill_terminates_notepad()
    {
        var p = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = false });
        Assert.NotNull(p);
        Thread.Sleep(600);

        var ok = _ctrl.ForceKill(p.Id);
        Assert.True(ok);
        Assert.True(p.WaitForExit(2000));
    }

    [Fact]
    public void Restore_relaunches_process()
    {
        var p = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = false });
        Assert.NotNull(p);
        var path = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\notepad.exe";
        Thread.Sleep(600);

        _ctrl.ForceKill(p.Id);
        p.WaitForExit(2000);

        var restarted = _ctrl.Restore(path);
        Assert.NotNull(restarted);
        Thread.Sleep(400);
        Assert.False(restarted!.HasExited);
        restarted.Kill();
    }
}
