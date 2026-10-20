using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Attribute;

/// <summary>
/// 判定进程是否为 Windows 自带。
///
/// P0 实现：仅按路径判定（%SystemRoot% 下视为系统进程）。
/// 设计文档 §5.2 要求"路径 + 微软签名"，但 WinVerifyTrust 单次 10–50ms × 200 进程过慢，
/// 签名校验移到 P1 异步补充。路径判定对常见场景足够：
///   - explorer/dwm/svchost/csrss 等都在 C:\Windows 下
///   - 用户软件极少装到 C:\Windows
///
/// 已知误判：若用户把第三方软件装到 %SystemRoot% 下会被判为系统——属极端场景，P0 接受。
/// </summary>
public static class ProcessClassifier
{
    private static readonly string SystemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>
    /// 是否为 Windows 自带进程。
    /// </summary>
    public static bool IsSystemProcess(ProcInfo proc)
    {
        if (string.IsNullOrEmpty(proc.FullPath))
            return false;
        return proc.FullPath.StartsWith(SystemRoot, StringComparison.OrdinalIgnoreCase);
    }
}
