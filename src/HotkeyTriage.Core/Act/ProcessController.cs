using System.Diagnostics;
using System.Runtime.InteropServices;
using HotkeyTriage.Core.Interop;

namespace HotkeyTriage.Core.Act;

public interface IProcessController
{
    bool GracefulClose(int pid, TimeSpan timeout);
    bool ForceKill(int pid);
    Process? Restore(string path, string? args = null, string? workingDir = null);
}

/// <summary>
/// 进程关闭与恢复。
///
/// 优先级（编排层强制）：GracefulClose > ForceKill。
/// 任何关闭动作执行前，编排层必须先落盘快照（DisabledListStore）。
/// </summary>
public sealed class ProcessController : IProcessController
{
    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>
    /// 优雅关闭：向主窗口投递 WM_CLOSE，等待退出。
    /// 不会强制结束，适合有 UI 的进程（给它保存数据的机会）。
    /// </summary>
    /// <returns>true 表示进程在超时内退出</returns>
    public bool GracefulClose(int pid, TimeSpan timeout)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            if (!proc.HasExited && proc.CloseMainWindow())
            {
                if (proc.WaitForExit((int)timeout.TotalMilliseconds))
                    return true;
            }
        }
        catch (ArgumentException) { /* 进程已退出 */ }
        catch (Exception) { /* 权限不足等 */ }
        return false;
    }

    /// <summary>
    /// 强制结束。用于优雅关闭超时，或无窗口进程。
    /// </summary>
    /// <returns>
    /// true 表示确实结束了一个进程。进程本来就不存在时返回 false ——
    /// 不能把"目标不存在"报成"已结束"，否则界面会对一个不相干的 PID 宣称成功。
    /// </returns>
    public bool ForceKill(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
            return proc.WaitForExit(3000);
        }
        catch (ArgumentException) { return false; /* 进程不存在，没有可结束的对象 */ }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// 恢复：用记录的路径与参数重新启动进程。
    /// </summary>
    public Process? Restore(string path, string? args = null, string? workingDir = null)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            // UseShellExecute=true 让 Shell 决定启动方式，
            // 对带 manifest / 需要关联的 exe 更稳（如微信、UWP 包装器）
            //
            // 已知限制（实测）：Win11 的 notepad.exe 等只是「应用执行别名」，
            // 由 Shell 拉起的是 WindowsApps 里的商店版进程，返回的 Process
            // 只是转发器 —— 它的 HasExited 不代表真实窗口是否存活，
            // Kill() 也关不掉真实窗口。判断"恢复成功"不能只看 HasExited。
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            };
            if (!string.IsNullOrEmpty(args))
                psi.Arguments = args;
            if (!string.IsNullOrEmpty(workingDir))
                psi.WorkingDirectory = workingDir;
            return Process.Start(psi);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
