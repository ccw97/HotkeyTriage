namespace HotkeyTriage.Core.Probe;

/// <summary>
/// 试注册探测结果。
/// Free  = 该组合当前无人占用
/// Taken = 该组合已被注册（错误码 1409）
/// Error = 探测失败，原因不在"被占用"（权限、保留键等），不参与判定
/// </summary>
public enum ProbeResult
{
    Free,
    Taken,
    Error
}
