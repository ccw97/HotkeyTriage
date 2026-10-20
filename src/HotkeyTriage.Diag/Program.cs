using System.Runtime.InteropServices;
using HotkeyTriage.Core.Discover;
using HotkeyTriage.Core.Interop;
using HotkeyTriage.Core.Probe;

// 用法：
//   HotkeyTriage.Diag             -> P2 归因端到端回归
//   HotkeyTriage.Diag --shot out.png -> 把主窗口离屏渲染成 PNG（检查排版）

if (args.Length >= 2 && args[0] == "--shot")
{
    UiShot.Capture(args[1], args.Length >= 3 && int.TryParse(args[2], out var ti) ? ti : 0);
    return 0;
}

if (args.Length >= 1 && args[0] == "--popup-child")
{
    return PopupChild.Run();
}

if (args.Length >= 1 && args[0] == "--dismiss-test")
{
    return DismissTest.Run();
}

// 端到端回归：自己注册一个热键，让 FastIdentify 去归因，看能否认出自己。
// 认得出 = P2 链路闭环；认不出 = 还有缺陷。
// 这个实验不依赖微信/AHK 等任何第三方软件，可反复运行。

const uint VK_F11 = 0x7A;
uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT;
string combo = KeyCombo.Format(mods, VK_F11);
int selfPid = Environment.ProcessId;

Console.WriteLine("=== FastIdentify 端到端回归 ===");
Console.WriteLine($"目标键: {combo}   自身 PID: {selfPid}");
Console.WriteLine();

// 注册线程必须有消息泵：WM_HOTKEY 投递到「注册热键的那个线程」
var ready = new ManualResetEventSlim(false);
bool regOk = false;
var pump = new Thread(() =>
{
    regOk = NativeMethods.RegisterHotKey(IntPtr.Zero, 1, mods, VK_F11);
    ready.Set();
    while (Pump.GetMessage(out var m, IntPtr.Zero, 0, 0) > 0)
    {
        Pump.TranslateMessage(ref m);
        Pump.DispatchMessage(ref m);
    }
}) { IsBackground = true, Name = "pump" };
pump.Start();
ready.Wait(3000);
Console.WriteLine($"[1] 注册热键 {combo} -> {(regOk ? "成功" : "失败")}");
if (!regOk) { Console.WriteLine("    无法建立测试前提"); return 1; }

try
{
    using var fast = new FastIdentify();
    var res = fast.Identify(new[] { (mods, VK_F11) }, Array.Empty<ProcInfo>());

    Console.WriteLine($"[2] Refused={res.Refused}  Reason=\"{res.Reason}\"");
    Console.WriteLine($"[3] 归因进程数={res.AttributionByPid.Count}  未归因={res.Unattributed.Count}");
    foreach (var kv in res.AttributionByPid)
        Console.WriteLine($"    PID {kv.Key} -> {string.Join(",", kv.Value.Select(k => KeyCombo.Format(k.Mods, k.Vk)))}");

    Console.WriteLine();
    bool selfFound = res.AttributionByPid.ContainsKey(selfPid);
    Console.WriteLine(selfFound
        ? ">>> 通过: 能认出自己注册的热键，P2 归因链路闭环"
        : ">>> 失败: 连自己注册的热键都认不出来");
    return selfFound ? 0 : 2;
}
finally
{
    NativeMethods.UnregisterHotKey(IntPtr.Zero, 1);
}

internal static class Pump
{
    [DllImport("user32.dll")]
    internal static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessage(ref MSG m);
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd; public uint message; public IntPtr wParam, lParam;
    public uint time; public int ptX, ptY;
}

/// <summary>把主窗口离屏渲染成 PNG，用于在无交互桌面时检查排版。</summary>
internal static class UiShot
{
    public static void Capture(string path, int tab = 0)
    {
        var t = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var f = new HotkeyTriage.App.MainForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-8000, -8000),
                ShowInTaskbar = false,
            };
            f.Show();

            for (int i = 0; i < 90; i++)
            {
                Application.DoEvents();
                Thread.Sleep(60);
            }

            if (tab > 0) { f.SelectTab(tab); Application.DoEvents(); Thread.Sleep(120); }

            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            f.Close();
            Console.WriteLine($"shot: {path}  {bmp.Width}x{bmp.Height}");
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
    }
}

/// <summary>
/// 「触发后自动取消」的真实验证。
/// 靶子必须跑在**另一个进程**里：FastIdentify 会主动跳过本进程的窗口（不能误关自己的界面），
/// 所以同进程弹窗永远测不出结果 —— 这是第一版实验失败的原因。
/// </summary>
internal static class DismissTest
{
    public static int Run()
    {
        uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT;
        string combo = KeyCombo.Format(mods, 0x7A);

        Console.WriteLine("=== 自动取消（ESC）验证 ===");
        Console.WriteLine($"靶子键: {combo}");
        Console.WriteLine();

        var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--popup-child")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var child = System.Diagnostics.Process.Start(psi)!;
        string? hello = child.StandardOutput.ReadLine();
        Console.WriteLine($"[1] 靶子进程 PID {child.Id} 就绪信号: {hello ?? "(无)"}");
        if (hello != "READY") { try { child.Kill(); } catch { } return 1; }

        Thread.Sleep(400);

        try
        {
            using var fast = new FastIdentify();
            var res = fast.Identify(new[] { (mods, 0x7Au) }, Array.Empty<ProcInfo>());
            Console.WriteLine($"[2] 归因进程数={res.AttributionByPid.Count}（应为 1，指向靶子进程）");

            // 等 ESC 生效
            for (int i = 0; i < 20 && !child.HasExited; i++) Thread.Sleep(100);

            bool closed = child.HasExited;
            Console.WriteLine($"[3] 靶子弹窗已被关闭={closed}");
            Console.WriteLine();

            if (res.AttributionByPid.Count == 1 && closed)
            {
                Console.WriteLine(">>> 通过: 弹窗被自动按 ESC 取消，用户无需手动关闭");
                return 0;
            }
            if (!closed)
            {
                Console.WriteLine(">>> 失败: 弹窗仍未关闭");
                return 2;
            }
            Console.WriteLine(">>> 归因与取消均成功");
            return 0;
        }
        finally
        {
            if (!child.HasExited) { try { child.Kill(); } catch { } }
        }
    }
}

/// <summary>靶子：注册热键，按下后弹窗，收到 ESC 即关闭并退出进程。</summary>
internal static class PopupChild
{
    public static int Run()
    {
        uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT;
        Application.EnableVisualStyles();

        var popup = new Form
        {
            Text = "HotkeyTriage Popup Probe",
            Width = 420,
            Height = 240,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-7000, -7000),   // 屏幕外，不打扰桌面
            ShowInTaskbar = false,
            KeyPreview = true,
        };
        popup.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) popup.Close(); };
        popup.FormClosed += (_, _) => Application.ExitThread();

        Application.AddMessageFilter(new HotkeyFilter(() =>
        {
            popup.Show();
            popup.Activate();
        }));

        if (!NativeMethods.RegisterHotKey(IntPtr.Zero, 1, mods, 0x7A))
            return 1;

        Console.WriteLine("READY");
        Console.Out.Flush();
        Application.Run(new ApplicationContext());
        return 0;
    }
}

internal sealed class HotkeyFilter(Action onHotkey) : IMessageFilter
{
    private const int WM_HOTKEY = 0x0312;
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) onHotkey();
        return false;
    }
}
