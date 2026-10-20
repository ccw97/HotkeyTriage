using System.Runtime.InteropServices;
using HotkeyTriage.Core.Interop;

namespace HotkeyTriage.Core.Probe;

/// <summary>
/// 用 RegisterHotKey 试注册反推某组合键是否被占用。
///
/// 线程约束：必须由同一 STA 线程串行调用。RegisterHotKey 把热键绑定到调用线程，
/// 线程退出会自动释放。本类不维护线程状态，调用方负责线程模型。
///
/// 副作用声明：探测会短暂占用该热键（微秒级），随即注销。不修改任何系统状态。
/// </summary>
public interface IHotkeyProbe
{
    ProbeResult Probe(uint modifiers, uint vk);
}

public sealed class HotkeyProbe : IHotkeyProbe
{
    private const int DefaultHotkeyId = 0xBEEF;

    private readonly int _hotkeyId;

    public HotkeyProbe() : this(DefaultHotkeyId) { }

    /// <param name="hotkeyId">注册用的 id；不同 id 互不冲突，便于测试与生产共存。</param>
    public HotkeyProbe(int hotkeyId)
    {
        _hotkeyId = hotkeyId;
    }

    /// <summary>
    /// 探测某组合键是否被占用。
    /// </summary>
    /// <returns>Free 表示空闲；Taken 表示被他人占用（errorCode=1409）；Error 表示探测失败</returns>
    /// <summary>无 out 参数的便捷重载，供 IHotkeyProbe 接口使用</summary>
    public ProbeResult Probe(uint modifiers, uint vk) => Probe(modifiers, vk, out _);

    public ProbeResult Probe(uint modifiers, uint vk, out int errorCode)
    {
        errorCode = 0;
        if (NativeMethods.RegisterHotKey(IntPtr.Zero, _hotkeyId, modifiers, vk))
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, _hotkeyId);
            return ProbeResult.Free;
        }
        errorCode = Marshal.GetLastWin32Error();
        return errorCode == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
            ? ProbeResult.Taken
            : ProbeResult.Error;
    }
}
