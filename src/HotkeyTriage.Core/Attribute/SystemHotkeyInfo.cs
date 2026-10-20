namespace HotkeyTriage.Core.Attribute;

/// <summary>
/// 系统热键判定结果。
/// </summary>
public enum SystemKind
{
    /// <summary>不是系统热键</summary>
    NotSystem,
    /// <summary>系统保留，不可禁用（Alt+Tab、Win+L 等）</summary>
    Reserved,
    /// <summary>系统功能，可关闭（Win+G、Win+V 等）</summary>
    Disableable
}

/// <summary>
/// 系统热键信息。
/// </summary>
/// <param name="Kind">判定类型</param>
/// <param name="Group">树状视图分组（窗口管理/系统/输入/截图录制/辅助功能）</param>
/// <param name="Description">人话说明</param>
/// <param name="Hint">关闭路径，仅 Disableable 有值</param>
public sealed record SystemHotkeyInfo(
    SystemKind Kind,
    string Group,
    string Description,
    string Hint
);
