using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Attribute;

/// <summary>
/// 进程风险等级。Guard 是唯一允许决定"能否动手"的地方。
/// </summary>
public enum RiskLevel
{
    /// <summary>无窗口后台进程，可直接纳入深扫</summary>
    Safe,
    /// <summary>有可见窗口，深扫前需在清单中明示，可被用户取消勾选</summary>
    Caution,
    /// <summary>永不进入任何关闭或注入流程</summary>
    Forbidden
}

/// <summary>
/// 安全闸门（v3.2 提权后必须绝对化）。
///
/// 提权状态下一次误判的后果是会话崩溃或封号，因此：
///   - 名单为编译期常量，不接受运行时配置
///   - 不提供"忽略警告继续"入口
///   - 反作弊名单必须硬编码，不得来自可编辑文件（用户误删一条就可能封号）
/// </summary>
public static class Guard
{
    /// <summary>系统关键进程：结束会导致会话崩溃</summary>
    private static readonly HashSet<string> SystemCritical = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe",
        "services.exe", "lsass.exe", "dwm.exe", "explorer.exe",
        "svchost.exe", "fontdrvhost.exe", "ctfmon.exe",
        "sihost.exe", "taskhostw.exe", "shellhost.exe",
        "runtimebroker.exe", "searchhost.exe", "startmenuexperiencehost.exe",
        "textinputhost.exe", "applicationframehost.exe"
    };

    /// <summary>反作弊组件：触碰即可能封号</summary>
    private static readonly HashSet<string> AntiCheat = new(StringComparer.OrdinalIgnoreCase)
    {
        // Easy Anti-Cheat
        "easyanticheat.exe", "eac_launcher.exe", "start_protected_game.exe",
        // BattlEye
        "beservice.exe", "beclient.exe", "_be.exe", "beservice_x64.exe",
        // Riot Vanguard
        "vgc.exe", "vgk.exe", "vgtray.exe",
        // 腾讯 ACE
        "ace-base.exe", "sgguard.exe", "ace-game.exe", "sguardsvc.exe",
        // 米哈游
        "mhyprot.exe", "mhypbase.exe",
        // 其他
        "nprotect.exe", "gamemon.des", "xigncode.exe", "x3.xem",
        "javelin.exe", "ricochet.exe", "pbsvc.exe", "punkbuster.exe",
        "gameguard.des"
    };

    /// <summary>
    /// 评估进程风险等级。Forbidden 一律不得进入关闭/注入流程。
    /// </summary>
    public static RiskLevel Evaluate(ProcInfo proc)
    {
        var exe = proc.ExeName.ToLowerInvariant();
        if (SystemCritical.Contains(exe) || AntiCheat.Contains(exe))
            return RiskLevel.Forbidden;
        return proc.HasVisibleWindow ? RiskLevel.Caution : RiskLevel.Safe;
    }

    /// <summary>仅供测试与诊断：是否在系统关键名单</summary>
    public static bool IsSystemCritical(string exe) =>
        SystemCritical.Contains(exe.ToLowerInvariant());

    /// <summary>仅供测试与诊断：是否在反作弊名单</summary>
    public static bool IsAntiCheat(string exe) =>
        AntiCheat.Contains(exe.ToLowerInvariant());
}
