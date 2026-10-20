# -*- coding: utf-8 -*-
"""
H3 验证实验：热键释放延迟
=========================
回答一个问题：结束占用热键的进程后，要等多久才能用 probe 探测到热键已释放？

这个数值直接决定 DeepScanner 每轮的等待参数（设计文档 §3.3）。
设小了会漏判（把 A 的键误判为无人占用）；设大了 20 个进程会拖到数分钟。

方法：
  1. 子进程注册 Ctrl+Alt+F9
  2. 主进程确认 taken
  3. 结束子进程，记录结束时刻 t0
  4. 在 t0 + delay 时刻探测，记录 free / taken
  5. 每个延迟重复 N 次，统计 free 比例
  6. 找"100% free"的最小延迟

精度处理：Windows 默认计时器分辨率 15.6ms，用 timeBeginPeriod(1) 提升到 1ms。
"""
import ctypes
import ctypes.wintypes as wt
import json
import statistics
import subprocess
import sys
import time

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
winmm = ctypes.WinDLL("winmm")

user32.RegisterHotKey.argtypes = [wt.HWND, ctypes.c_int, wt.UINT, wt.UINT]
user32.RegisterHotKey.restype = wt.BOOL
user32.UnregisterHotKey.argtypes = [wt.HWND, ctypes.c_int]
user32.UnregisterHotKey.restype = wt.BOOL
kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.OpenProcess.restype = wt.HANDLE
kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.TerminateProcess.argtypes = [wt.HANDLE, wt.UINT]
kernel32.TerminateProcess.restype = wt.BOOL
kernel32.WaitForSingleObject.argtypes = [wt.HANDLE, wt.DWORD]
kernel32.QueryPerformanceCounter.argtypes = [ctypes.POINTER(ctypes.c_longlong)]
kernel32.QueryPerformanceFrequency.argtypes = [ctypes.POINTER(ctypes.c_longlong)]

winmm.timeBeginPeriod.argtypes = [wt.UINT]
winmm.timeBeginPeriod.restype = wt.UINT
winmm.timeEndPeriod.argtypes = [wt.UINT]
winmm.timeEndPeriod.restype = wt.UINT

MOD_CTRL, MOD_ALT = 0x0002, 0x0001
MOD = MOD_CTRL | MOD_ALT
MOD_NO_REPEAT = 0x4000
PROCESS_TERMINATE = 0x0001
VK_F9 = 0x78
PROBE_ID = 0xBEEF
INFINITE = 0xFFFFFFFF


def qpc_ms():
    """高精度计时（毫秒，浮点）"""
    c = ctypes.c_longlong()
    f = ctypes.c_longlong()
    kernel32.QueryPerformanceFrequency(ctypes.byref(f))
    kernel32.QueryPerformanceCounter(ctypes.byref(c))
    return c.value * 1000.0 / f.value


def probe(mods, vk):
    ctypes.set_last_error(0)
    if user32.RegisterHotKey(None, PROBE_ID, mods, vk):
        user32.UnregisterHotKey(None, PROBE_ID)
        return "free"
    return "taken" if ctypes.get_last_error() == 1409 else "error"


def child_main(vk):
    if not user32.RegisterHotKey(None, PROBE_ID, MOD | MOD_NO_REPEAT, vk):
        sys.exit(2)
    print("READY", flush=True)
    while True:
        time.sleep(0.3)


def spawn_child(vk):
    p = subprocess.Popen(
        [sys.executable, __file__, "child", hex(vk)],
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
    )
    for _ in range(80):
        line = p.stdout.readline()
        if line.startswith("READY"):
            return p
        if not line:
            break
    p.kill()
    return None


def kill_and_wait(proc):
    """结束进程并等待其真正退出（句柄 signaled）"""
    h = kernel32.OpenProcess(PROCESS_TERMINATE, False, proc.pid)
    if not h:
        return None
    kernel32.TerminateProcess(h, 1)
    # 等待进程对象 signaled（最多 2 秒）
    kernel32.WaitForSingleObject(h, 2000)
    kernel32.CloseHandle(h)
    proc.wait(timeout=3)


def measure_one(delay_ms):
    """结束子进程后，等待 delay_ms，探测。返回 (probe 结果, 实际经过的毫秒)"""
    child = spawn_child(VK_F9)
    if not child:
        return None, None
    time.sleep(0.2)
    # 确认确实 taken
    if probe(MOD, VK_F9) != "taken":
        child.kill()
        return None, None

    t0 = qpc_ms()
    kill_and_wait(child)
    t1 = qpc_ms()
    kill_elapsed = t1 - t0

    # 等到目标时刻
    target = delay_ms - kill_elapsed
    if target > 0:
        time.sleep(target / 1000.0)
    t_probe = qpc_ms()
    actual_since_kill = t_probe - t1
    result = probe(MOD, VK_F9)
    return result, actual_since_kill


def run():
    winmm.timeBeginPeriod(1)
    try:
        delays = [0, 2, 5, 10, 20, 30, 50, 75, 100, 150, 200, 300, 500]
        repeats = 12

        print()
        print("=" * 70)
        print("  H3 实验：热键释放延迟（结束进程后多久能 probe 到 free）")
        print(f"  每个延迟重复 {repeats} 次")
        print("=" * 70)
        print(f"  {'目标延迟ms':>10}  {'free次数':>8}  {'taken':>6}  "
              f"{'实际平均ms':>10}  {'实际最大ms':>10}")
        print("  " + "-" * 66)

        results = []
        for delay in delays:
            free_count = 0
            actuals = []
            for _ in range(repeats):
                res, actual = measure_one(delay)
                if res is None:
                    continue
                if res == "free":
                    free_count += 1
                actuals.append(actual)
            actual_avg = statistics.mean(actuals) if actuals else 0
            actual_max = max(actuals) if actuals else 0
            results.append({
                "target_ms": delay,
                "free": free_count,
                "taken": repeats - free_count,
                "actual_avg_ms": round(actual_avg, 2),
                "actual_max_ms": round(actual_max, 2),
            })
            print(f"  {delay:>10}  {free_count:>8}  {repeats - free_count:>6}  "
                  f"{actual_avg:>10.2f}  {actual_max:>10.2f}")

        # 找 100% free 的最小延迟
        reliable = [r for r in results if r["taken"] == 0]
        if reliable:
            recommended = min(r["target_ms"] for r in reliable)
            print()
            print(f"  → 100% 可靠的最小目标延迟：{recommended} ms")
        else:
            worst = min(r["free"] / repeats for r in results)
            print()
            print(f"  ⚠ 所有延迟都有漏判，最高 free 比例 {worst:.0%}，需加大延迟")
            recommended = max(r["target_ms"] for r in results) * 2

        # 工程建议：取可靠值 + 50% 安全裕度
        safe = int(recommended * 1.5 + 5)
        print(f"  → 工程建议等待值（含 50% 裕度）：{safe} ms")
        print(f"  → 对 20 个候选进程的深扫总耗时影响：+{safe * 20 / 1000:.1f} 秒")
        print("=" * 70)

        out = {
            "experiment": "H3 hotkey release delay",
            "repeats_per_delay": repeats,
            "results": results,
            "recommended_wait_ms": safe,
            "min_reliable_delay_ms": recommended if reliable else None,
        }
        Path = type(__import__("pathlib").Path)
        here = __import__("pathlib").Path(__file__).resolve().parent.parent
        rec = here / "docs" / "实验记录.md"
        rec.parent.mkdir(parents=True, exist_ok=True)
        with open(rec, "a", encoding="utf-8") as f:
            f.write("\n\n## H3 热键释放延迟（自动生成）\n\n")
            f.write("```json\n" + json.dumps(out, ensure_ascii=False, indent=2) + "\n```\n")
            f.write(f"\n**结论**：深扫每轮等待 **{safe} ms**（推荐值，含裕度）。\n")
        print(f"\n  已写入 {rec}")
    finally:
        winmm.timeEndPeriod(1)


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "child":
        child_main(int(sys.argv[2], 16))
    else:
        run()
