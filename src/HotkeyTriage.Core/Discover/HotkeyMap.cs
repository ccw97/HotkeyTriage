namespace HotkeyTriage.Core.Discover;

/// <summary>热键条目类型</summary>
public enum HotkeyKind
{
    System,         // 系统保留或系统功能
    ThirdParty,     // 第三方占用（深扫前 owner 未知）
    Unknown         // 无法归因
}

/// <summary>归因证据强度</summary>
public enum HotkeyEvidence
{
    None,           // 仅探测到 taken，未归因
    Rule,           // 系统规则表判定
    Traverse,       // 遍历探测归因
    KnowledgeBase   // 知识库命中（可选）
}

/// <summary>
/// 热键地图中的一条记录。
/// </summary>
public sealed record HotkeyEntry(
    uint Mods,
    uint Vk,
    string Combo,           // "Ctrl+Alt+A"
    HotkeyKind Kind,
    HotkeyEvidence Evidence,
    string Group,           // 树状视图分组
    string Description,     // 人话说明
    string Hint,            // 关闭路径（系统功能或知识库命中时）
    int? OwnerPid,          // 深扫后填入；快扫时为 null
    string OwnerExe,
    string OwnerPath
);

/// <summary>
/// 一次扫描的完整结果。
/// </summary>
public sealed class HotkeyMap
{
    public string Mode { get; init; } = "quick";       // quick | deep
    public int GridSize { get; init; }
    public IReadOnlyList<HotkeyEntry> Entries { get; init; } = Array.Empty<HotkeyEntry>();
}
