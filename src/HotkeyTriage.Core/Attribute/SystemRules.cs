using HotkeyTriage.Core.Interop;

namespace HotkeyTriage.Core.Attribute;

/// <summary>
/// 系统热键规则表（静态，不腐化）。
///
/// 区别于知识库：这里是判定逻辑（"Alt+Tab 是系统保留"），不是事实数据
/// （"微信注册了 Alt+A"）。Windows 大版本才变，不需要随软件版本维护。
///
/// 用于：
///   - 把系统热键从第三方候选中剥离（避免引导用户关 explorer）
///   - 树状视图「Windows 自带」tab 的分组与说明
/// </summary>
public static class SystemRules
{
    private const uint MOD_ALT = NativeMethods.MOD_ALT;
    private const uint MOD_CTRL = NativeMethods.MOD_CONTROL;
    private const uint MOD_SHIFT = NativeMethods.MOD_SHIFT;
    private const uint MOD_WIN = NativeMethods.MOD_WIN;

    // VK 常量，避免魔法数字
    private const uint VK_TAB = 0x09;
    private const uint VK_ESCAPE = 0x1B;
    private const uint VK_SPACE = 0x20;
    private const uint VK_DELETE = 0x2E;
    private const uint VK_F4 = 0x73;
    private const uint VK_D = 0x44;
    private const uint VK_E = 0x45;
    private const uint VK_L = 0x4C;
    private const uint VK_R = 0x52;
    private const uint VK_A = 0x41;
    private const uint VK_G = 0x47;
    private const uint VK_H = 0x48;
    private const uint VK_I = 0x49;
    private const uint VK_P = 0x50;
    private const uint VK_V = 0x56;
    private const uint VK_X = 0x58;
    private const uint VK_S = 0x53;

    /// <summary>系统保留键：不可禁用，禁止进入任何关闭/注入流程</summary>
    private static readonly Dictionary<(uint mods, uint vk), SystemHotkeyInfo> _reserved = new()
    {
        { (MOD_ALT, VK_TAB), new(SystemKind.Reserved, "窗口管理", "Alt+Tab 任务切换", "") },
        { (MOD_ALT, VK_F4), new(SystemKind.Reserved, "窗口管理", "Alt+F4 关闭窗口", "") },
        { (MOD_ALT | MOD_SHIFT, VK_TAB), new(SystemKind.Reserved, "窗口管理", "Alt+Shift+Tab 反向切换", "") },
        { (MOD_CTRL | MOD_ALT, VK_TAB), new(SystemKind.Reserved, "窗口管理", "Ctrl+Alt+Tab 任务视图", "") },
        { (MOD_CTRL | MOD_ALT | MOD_SHIFT, VK_TAB), new(SystemKind.Reserved, "窗口管理", "Ctrl+Alt+Shift+Tab", "") },
        { (MOD_CTRL | MOD_ALT, VK_DELETE), new(SystemKind.Reserved, "系统", "Ctrl+Alt+Del 安全注意序列", "") },
        { (MOD_CTRL | MOD_SHIFT, VK_ESCAPE), new(SystemKind.Reserved, "系统", "Ctrl+Shift+Esc 任务管理器", "") },
        { (MOD_CTRL, VK_ESCAPE), new(SystemKind.Reserved, "系统", "Ctrl+Esc 开始菜单", "") },
        { (MOD_WIN, VK_TAB), new(SystemKind.Reserved, "窗口管理", "Win+Tab 任务视图", "") },
        { (MOD_WIN, VK_SPACE), new(SystemKind.Reserved, "输入", "Win+Space 输入法切换", "") },
        { (MOD_WIN, VK_L), new(SystemKind.Reserved, "系统", "Win+L 锁屏", "") },
        { (MOD_WIN, VK_D), new(SystemKind.Reserved, "窗口管理", "Win+D 显示桌面", "") },
        { (MOD_WIN, VK_E), new(SystemKind.Reserved, "系统", "Win+E 文件资源管理器", "") },
        { (MOD_WIN, VK_R), new(SystemKind.Reserved, "系统", "Win+R 运行", "") },
        { (MOD_WIN, VK_A), new(SystemKind.Reserved, "系统", "Win+A 快速设置", "") },
        { (MOD_WIN, VK_I), new(SystemKind.Reserved, "系统", "Win+I 系统设置", "") },
        { (MOD_WIN, VK_X), new(SystemKind.Reserved, "系统", "Win+X 高级用户菜单", "") },
    };

    /// <summary>系统功能：可关闭，给出关闭路径</summary>
    private static readonly Dictionary<(uint mods, uint vk), SystemHotkeyInfo> _disableable = new()
    {
        { (MOD_WIN, VK_G), new(SystemKind.Disableable, "截图录制", "Xbox Game Bar 录制栏", "设置 → 游戏 → Xbox Game Bar → 关") },
        { (MOD_WIN, VK_V), new(SystemKind.Disableable, "辅助功能", "剪贴板历史", "设置 → 系统 → 剪贴板 → 关闭历史") },
        { (MOD_WIN, VK_H), new(SystemKind.Disableable, "辅助功能", "语音输入", "设置 → 辅助功能 → 语音 → 关闭") },
        { (MOD_WIN, VK_P), new(SystemKind.Disableable, "系统", "投影/多显示器", "设置 → 系统 → 显示 → 投影") },
        { (MOD_WIN | MOD_SHIFT, VK_S), new(SystemKind.Disableable, "截图录制", "截图工具", "设置 → 辅助功能 → 键盘 → 截图快捷键") },
    };

    private static readonly SystemHotkeyInfo NotSystemInfo =
        new(SystemKind.NotSystem, "", "", "");

    /// <summary>
    /// 判定某组合键是否为系统热键。
    /// </summary>
    public static SystemHotkeyInfo Classify(uint mods, uint vk)
    {
        if (_reserved.TryGetValue((mods, vk), out var r))
            return r;
        if (_disableable.TryGetValue((mods, vk), out var d))
            return d;

        // Win+ 前缀兜底：未在表中的 Win+ 组合也归为系统壳热键
        if ((mods & MOD_WIN) != 0)
            return new(SystemKind.Reserved, "系统", "Windows 壳热键（Explorer/DWM 注册）", "");

        return NotSystemInfo;
    }
}
