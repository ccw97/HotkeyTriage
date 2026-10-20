using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Discover;

/// <summary>深扫进度</summary>
public sealed record DeepScanProgress(int Processed, int Total, string CurrentExe, int RemainingKeys);

/// <summary>深扫结果</summary>
public sealed class DeepScanResult
{
    /// <summary>PID → 该进程释放的热键列表</summary>
    public IReadOnlyDictionary<int, List<(uint Mods, uint Vk)>> AttributionByPid { get; init; }
        = new Dictionary<int, List<(uint Mods, uint Vk)>>();

    /// <summary>仍无法归因的键</summary>
    public IReadOnlyList<(uint Mods, uint Vk)> Unattributed { get; init; }
        = Array.Empty<(uint Mods, uint Vk)>();

    /// <summary>是否全部归因完成（未中止且无残留）</summary>
    public bool Completed { get; init; }

    /// <summary>已处理的候选进程数</summary>
    public int ScannedCount { get; init; }
}
