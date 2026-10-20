namespace HotkeyTriage.Core.Probe;

/// <summary>
/// 进程快照条目。
/// </summary>
/// <param name="Pid">进程 ID</param>
/// <param name="ExeName">小写可执行文件名</param>
/// <param name="FullPath">完整路径，获取失败时为空字符串（受保护进程属正常）</param>
/// <param name="HasVisibleWindow">是否有可见顶层窗口</param>
public sealed record ProcInfo(
    int Pid,
    string ExeName,
    string FullPath,
    bool HasVisibleWindow
);
