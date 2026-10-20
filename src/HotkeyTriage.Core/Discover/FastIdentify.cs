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

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "hotkey_triage.log"),
                $"{DateTime.Now:HH:mm:ss.fff} [Fast] {msg}\r\n");
        }
        catch { }
    }

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
    private static extern int ReadRecords(int fromSeq, IntPtr outBuf, int maxCount);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    private static extern int GetCount();

    // ── SendInput 结构 ────────────────────────────────────────────
    // 关键：64 位下 sizeof(INPUT) 必须是 40 字节（4 字节 type + 4 字节对齐 + 32 字节 union）。
    // 若只声明 type + KEYBDINPUT，sizeof 会是 32，SendInput 直接返回 0 且
    // GetLastError = 87 (ERROR_INVALID_PARAMETER) —— 一个键都发不出去。
    // 这是 P2 归因长期为空的真正根因，必须保留 MOUSEINPUT 撑满 union。
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;      // 1 = INPUT_KEYBOARD
        public INPUTUNION u;
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
            {
                Log("SetHook 失败");
                return new FastIdentifyResult { Refused = true, Reason = "SetWindowsHookEx 失败，可能权限不足" };
            }
            _hooked = true;
            Log($"SetHook 成功，钩子已安装；sizeof(INPUT)={Marshal.SizeOf<INPUT>()}");

            // 预热：全局钩子是惰性注入的——目标进程要等下一次 GetMessage/PeekMessage
            // 才会加载 DLL。不预热就立刻发键，头几个键会打空。
            Thread.Sleep(400);

            var attribution = new Dictionary<int, List<(uint Mods, uint Vk)>>();
            var remaining = keys.ToList();
            int seq = GetCount();

            // 4. 逐个模拟按键
            foreach (var key in keys)
            {
                if (remaining.Count == 0) break;
                uint sent = SendCombo(key.Mods, key.Vk);
                if (sent == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    Log($"SendInput 失败 {KeyCombo.Format(key.Mods, key.Vk)} err={err}");
                }
                Thread.Sleep(180); // 等 WM_HOTKEY 投递与钩子回传

                var recs = ReadNew(ref seq);
                bool hit = false;
                foreach (var r in recs)
                {
                    if (r.Mods == key.Mods && r.Vk == key.Vk && r.Pid != 0)
                    {
                        var pid = (int)r.Pid;
                        if (!attribution.TryGetValue(pid, out var list))
                            attribution[pid] = list = new List<(uint, uint)>();
                        if (!list.Contains(key)) list.Add(key);
                        remaining.Remove(key);
                        hit = true;
                        break;
                    }
                }
                Log($"模拟 {KeyCombo.Format(key.Mods, key.Vk)} -> 发出 {sent} 事件，捕获 {recs.Count} 条，{(hit ? "命中" : "未命中")}");
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

    /// <summary>
    /// 取出自 seq 之后的新增记录，并推进 seq。
    /// seq 单调递增（DLL 侧不清零），因此不存在"读写索引错位"的可能。
    /// </summary>
    private List<HotkeyRecord> ReadNew(ref int seq)
    {
        const int MAX = 256;
        int recSize = Marshal.SizeOf<HotkeyRecord>();
        var buf = Marshal.AllocHGlobal(recSize * MAX);
        try
        {
            int take = ReadRecords(seq, buf, MAX);
            var list = new List<HotkeyRecord>(Math.Max(take, 0));
            for (int i = 0; i < take; i++)
                list.Add(Marshal.PtrToStructure<HotkeyRecord>(buf + i * recSize));
            seq = GetCount();
            return list;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static uint SendCombo(uint mods, uint vk)
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

        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Down(ushort vk) => new()
    { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk } } };

    private static INPUT Up(ushort vk) => new()
    { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } };

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
