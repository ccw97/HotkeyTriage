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
    private const int VK_ESCAPE = 0x1B;

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

        // 收尾基线：整轮结束后把仍然残留的弹窗也一并收掉
        var baseline = SnapshotTopWindows();

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
            int dismissed = 0;
            foreach (var key in keys)
            {
                if (remaining.Count == 0) break;

                // 记录按键前的顶层窗口集合，用于判断"这个键是否真的催生了新窗口"
                var before = SnapshotTopWindows();

                uint sent = SendCombo(key.Mods, key.Vk);
                if (sent == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    Log($"SendInput 失败 {KeyCombo.Format(key.Mods, key.Vk)} err={err}");
                }
                Thread.Sleep(180); // 等 WM_HOTKEY 投递与钩子回传

                // 模拟按键会真的触发对方功能（微信弹截图框、录屏开始录）。
                // 这里检测新增的最上层窗口，用 ESC 把它收回，免去用户手动取消。
                var popup = FindNewTopWindow(before);
                if (popup != IntPtr.Zero)
                {
                    bool closed = DismissPopup(popup);
                    if (closed) dismissed++;
                    Log($"模拟 {KeyCombo.Format(key.Mods, key.Vk)} -> 触发弹窗，{(closed ? "已发送 ESC 取消" : "ESC 取消未生效")}");
                }

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
            Log($"本轮共取消 {dismissed} 个被触发的弹窗");

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
            DismissLeftovers(baseline);
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

    // ── 触发后的自动取消 ──────────────────────────────────────────
    // 快速识别的副作用是"真的把功能触发了一遍"。既然如此，就在触发后负责收尾：
    // 检测按键是否催生了新的顶层窗口，若有则把它拉回前台并按 ESC 关闭。

    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    /// <summary>当前所有可见且有标题的顶层窗口（Z 序，最上层在前）。</summary>
    private static HashSet<IntPtr> SnapshotTopWindows()
    {
        var set = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (NativeMethods.IsWindowVisible(h) && NativeMethods.GetWindowTextLengthW(h) > 0)
                set.Add(h);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    /// <summary>找出按键后新冒出来的最上层窗口（排除本进程自己的窗口）。</summary>
    private static IntPtr FindNewTopWindow(HashSet<IntPtr> before)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h)) return true;
            if (NativeMethods.GetWindowTextLengthW(h) == 0) return true;
            if (before.Contains(h)) return true;

            NativeMethods.GetWindowThreadProcessId(h, out uint pid);
            if (pid == OwnPid) return true;

            found = h;
            return false; // EnumWindows 自 Z 序顶向下，第一个即是用户看到的那个
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>把弹窗拉回前台并按 ESC 关闭。返回是否成功关闭。</summary>
    private static bool DismissPopup(IntPtr hwnd)
    {
        // 前台锁定会挡住 SetForegroundWindow，先临时附加到前台线程的输入队列
        uint curThread = NativeMethods.GetCurrentThreadId();
        uint fgThread = NativeMethods.GetWindowThreadProcessId(
            NativeMethods.GetForegroundWindow(), out _);
        bool attached = fgThread != 0 && fgThread != curThread
                        && NativeMethods.AttachThreadInput(curThread, fgThread, true);
        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(curThread, fgThread, false);
        }

        Thread.Sleep(60);

        // ESC 走完整输入栈，比 PostMessage 更通用（对方多用自己的消息循环）
        var esc = new[]
        {
            new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_ESCAPE } } },
            new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_ESCAPE, dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)esc.Length, esc, Marshal.SizeOf<INPUT>());

        Thread.Sleep(120);
        return !NativeMethods.IsWindowVisible(hwnd);
    }

    /// <summary>收尾：把本轮触发后仍残留的弹窗逐个关掉（最多 3 层）。</summary>
    private static void DismissLeftovers(HashSet<IntPtr> baseline)
    {
        for (int i = 0; i < 3; i++)
        {
            var w = FindNewTopWindow(baseline);
            if (w == IntPtr.Zero) return;
            if (!DismissPopup(w))
            {
                Log("收尾阶段仍有弹窗未能关闭，可能对方不响应 ESC");
                return;
            }
            Log("收尾阶段关闭了残留弹窗");
        }
    }

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
