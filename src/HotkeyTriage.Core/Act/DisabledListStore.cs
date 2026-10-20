using System.Text.Json;

namespace HotkeyTriage.Core.Act;

/// <summary>
/// 禁用快照：记录一次"结束进程"动作的全部信息，用于恢复。
/// 必须在关闭动作执行前落盘。
/// </summary>
public sealed record DisabledSnapshot(
    string Id,
    DateTime DisabledAt,
    bool Restored,
    int Pid,
    string ExeName,
    string FullPath,
    string? Args,
    string? WorkingDir,
    string Reason
);

/// <summary>
/// 禁用清单存储：落盘、列举未恢复项、标记已恢复。
///
/// 存储位置：%LOCALAPPDATA%\HotkeyTriage\disabled\
/// 启动时扫描 Restored=false 的条目，提示用户"上次禁用的程序尚未恢复"。
/// </summary>
public sealed class DisabledListStore
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public DisabledListStore(string? dir = null)
    {
        _dir = dir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HotkeyTriage", "disabled");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>落盘快照。写入失败必须抛错，不允许静默继续。</summary>
    public string Save(DisabledSnapshot snapshot)
    {
        var path = Path.Combine(_dir, $"{snapshot.Id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOpts));
        return snapshot.Id;
    }

    /// <summary>列出所有未恢复的快照</summary>
    public IReadOnlyList<DisabledSnapshot> ListUnrestored()
    {
        var result = new List<DisabledSnapshot>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var snap = JsonSerializer.Deserialize<DisabledSnapshot>(File.ReadAllText(file));
                if (snap is { Restored: false })
                    result.Add(snap);
            }
            catch
            {
                // 损坏文件跳过，不阻断启动
            }
        }
        return result;
    }

    /// <summary>标记某条快照为已恢复</summary>
    public void MarkRestored(string id)
    {
        var path = Path.Combine(_dir, $"{id}.json");
        if (!File.Exists(path))
            return;
        var snap = JsonSerializer.Deserialize<DisabledSnapshot>(File.ReadAllText(path));
        if (snap is null)
            return;
        File.WriteAllText(path, JsonSerializer.Serialize(snap with { Restored = true }, JsonOpts));
    }
}
