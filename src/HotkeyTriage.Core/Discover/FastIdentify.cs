using System.Runtime.InteropServices;
using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Interop;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Discover;

/// <summary>
/// 快速识别（P2）：注入 WH_GETMESSAGE 钩子 + 模拟按键 + 捕获 WM_HOTKEY 归属。
/// 不关闭任何程序，但模拟按键会触发占用者的功能。
///
/// 前置：已提权；无反作弊进程运行。
/// 收尾：强制释放所有修饰键，防止卡键。
/// </summary>
public sealed class FastIdentify : IDisposable
{
    private const string DllName = "hook.dll";
    private IntPtr _dll = IntPtr.Zero;
    private bool _hooked = false;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HotkeyRecord
    {
        public uint Pid;
        public uint Mods;
        public uint Vk;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    private static extern bool SetHook();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    private static extern bool RemoveHook();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    private static extern int GetRecords(IntPtr outBuf, int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type; // 1 = INPUT_KEYBOARD
        public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lib);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;

    public FastIdentifyResult Identify(
        IReadOnlyList<(uint Mods, uint Vk)> keys,
        IReadOnlyList<ProcInfo> candidates)
    {
        // 1. 环境闸门：检测到反作弊进程则拒绝
        if (candidates.Any(p => Guard.IsAntiCheat(p.ExeName)))
            return new FastIdentifyResult { Refused = true, Reason = "检测到反作弊进程运行，快速识别已禁用以保安全" };

        // 2. 加载 DLL
        _dll = LoadLibrary(DllName);
        if (_dll == IntPtr.Zero)
            return new FastIdentifyResult { Refused = true, Reason = "hook.dll 加载失败（应与 exe 同目录）" };

        try
        {
            // 3. 安装钩子
            if (!SetHook())
                return new FastIdentifyResult { Refused = true, Reason = "SetWindowsHookEx 失败，可能权限不足" };
            _hooked = true;

            var attribution = new Dictionary<int, List<(uint Mods, uint Vk)>>();
            var remaining = keys.ToList();

            // 4. 逐个模拟按键
            foreach (var key in keys)
            {
                if (remaining.Count == 0) break;
                SendCombo(key.Mods, key.Vk);
                Thread.Sleep(200); // 等 WM_HOTKEY 投递与钩子回传

                var recs = ReadRecords();
                foreach (var r in recs)
                {
                    if (r.Mods == key.Mods && r.Vk == key.Vk && r.Pid != 0)
                    {
                        var pid = (int)r.Pid;
                        if (!attribution.ContainsKey(pid))
                            attribution[pid] = new List<(uint, uint)>();
                        if (!attribution[pid].Contains(key))
                            attribution[pid].Add(key);
                        remaining.Remove(key);
                        break;
                    }
                }
            }

            return new FastIdentifyResult
            {
                AttributionByPid = attribution,
                Unattributed = remaining,
                Completed = remaining.Count == 0
            };
        }
        finally
        {
            if (_hooked) RemoveHook();
            ReleaseAllModifiers();
        }
    }

    private List<HotkeyRecord> ReadRecords()
    {
        const int MAX = 256;
        int recSize = Marshal.SizeOf<HotkeyRecord>();
        var buf = Marshal.AllocHGlobal(recSize * MAX);
        try
        {
            int n = GetRecords(buf, MAX);
            var list = new List<HotkeyRecord>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(Marshal.PtrToStructure<HotkeyRecord>(buf + i * recSize));
            }
            return list;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private void SendCombo(uint mods, uint vk)
    {
        var inputs = new List<INPUT>(10);
        if ((mods & NativeMethods.MOD_CONTROL) != 0) inputs.Add(Down(VK_CONTROL));
        if ((mods & NativeMethods.MOD_ALT) != 0) inputs.Add(Down(VK_MENU));
        if ((mods & NativeMethods.MOD_SHIFT) != 0) inputs.Add(Down(VK_SHIFT));
        if ((mods & NativeMethods.MOD_WIN) != 0) inputs.Add(Down(VK_LWIN));

        inputs.Add(Down((ushort)vk));
        inputs.Add(Up((ushort)vk));

        if ((mods & NativeMethods.MOD_WIN) != 0) inputs.Add(Up(VK_LWIN));
        if ((mods & NativeMethods.MOD_SHIFT) != 0) inputs.Add(Up(VK_SHIFT));
        if ((mods & NativeMethods.MOD_ALT) != 0) inputs.Add(Up(VK_MENU));
        if ((mods & NativeMethods.MOD_CONTROL) != 0) inputs.Add(Up(VK_CONTROL));

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Down(ushort vk) => new()
    { type = INPUT_KEYBOARD, ki = new() { wVk = vk } };

    private static INPUT Up(ushort vk) => new()
    { type = INPUT_KEYBOARD, ki = new() { wVk = vk, dwFlags = KEYEVENTF_KEYUP } };

    private void ReleaseAllModifiers()
    {
        var toRelease = new[] { VK_CONTROL, VK_MENU, VK_SHIFT, VK_LWIN };
        var ups = new List<INPUT>(4);
        foreach (var vk in toRelease)
        {
            if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                ups.Add(Up((ushort)vk));
        }
        if (ups.Count > 0)
            SendInput((uint)ups.Count, ups.ToArray(), Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_hooked) RemoveHook();
        if (_dll != IntPtr.Zero) FreeLibrary(_dll);
    }
}

public sealed class FastIdentifyResult
{
    public bool Refused { get; init; }
    public string Reason { get; init; } = "";
    public IReadOnlyDictionary<int, List<(uint Mods, uint Vk)>> AttributionByPid { get; init; }
        = new Dictionary<int, List<(uint Mods, uint Vk)>>();
    public IReadOnlyList<(uint Mods, uint Vk)> Unattributed { get; init; }
        = Array.Empty<(uint Mods, uint Vk)>();
    public bool Completed { get; init; }
}
