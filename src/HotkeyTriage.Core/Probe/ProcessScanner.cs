using System.Runtime.InteropServices;
using System.Text;
using HotkeyTriage.Core.Interop;

namespace HotkeyTriage.Core.Probe;

/// <summary>
/// 枚举运行中进程：PID、exe 名、完整路径、是否有可见顶层窗口。
///
/// 设计约束（来自设计文档 §5.2）：
///   - 路径获取失败不抛异常，降级为空字符串
///   - 用 PROCESS_QUERY_LIMITED_INFORMATION，避免对受保护进程请求过高权限而失败
///   - 托盘图标检测 P0 不实现（Shell 无公开枚举 API），用 HasVisibleWindow 近似
///   - 不做签名校验（WinVerifyTrust 单次 10–50ms × 200 进程 = 太慢），移到 P1 异步补充
/// </summary>
public sealed class ProcessScanner
{
    private static readonly IntPtr InvalidHandle = new(-1);

    public IReadOnlyList<ProcInfo> Scan()
    {
        var windowPids = CollectVisibleWindowPids();
        var result = new List<ProcInfo>(256);

        var snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == InvalidHandle)
            return result;

        try
        {
            var entry = new NativeMethods.PROCESSENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32W>()
            };
            if (!NativeMethods.Process32FirstW(snap, ref entry))
                return result;

            do
            {
                int pid = (int)entry.th32ProcessID;
                string exe = (entry.szExeFile ?? string.Empty).ToLowerInvariant();
                string path = TryGetFullPath(entry.th32ProcessID);
                bool hasWindow = windowPids.Contains(pid);
                result.Add(new ProcInfo(pid, exe, path, hasWindow));
            }
            while (NativeMethods.Process32NextW(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }

        return result;
    }

    private static string TryGetFullPath(uint pid)
    {
        // PID 0 (Idle) 与 4 (System) 拿不到路径，直接返回空，避免无谓的 OpenProcess
        if (pid <= 4)
            return string.Empty;

        var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero || h == InvalidHandle)
            return string.Empty;
        try
        {
            var buf = new StringBuilder(1024);
            uint size = (uint)buf.Capacity;
            return NativeMethods.QueryFullProcessImageNameW(h, 0, buf, ref size)
                ? buf.ToString()
                : string.Empty;
        }
        finally
        {
            NativeMethods.CloseHandle(h);
        }
    }

    private static HashSet<int> CollectVisibleWindowPids()
    {
        var pids = new HashSet<int>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hWnd))
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid != 0)
                    pids.Add((int)pid);
            }
            return true; // 继续枚举
        }, IntPtr.Zero);
        return pids;
    }
}
