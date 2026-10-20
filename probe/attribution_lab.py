# -*- coding: utf-8 -*-
"""
热键归属映射实验：验证「遍历进程 + 探测」能否在零注入、零驱动前提下
建立「进程 -> 它注册了哪些热键」的完整映射。

这是"取消知识库、纯技术手段罗列全系统热键"这一设想的可行性验证。

方法：
  1. 两个子进程分别注册 Ctrl+Alt+F9 与 Ctrl+Alt+F10
  2. 探测 -> 两键均为 taken，但不知道分别是谁
  3. 结束 A -> 探测 -> 若 F9 变 free 而 F10 仍 taken，则归属确立
  4. 重启 A -> 探测 -> 若 F9 重新 taken，则"结束+恢复"闭环成立
  5. 结束 B -> 探测 -> F10 变 free

用法：
  python attribution_lab.py child <vk_hex>   # 内部用
  python attribution_lab.py run              # 跑实验
"""
import ctypes
import ctypes.wintypes as wt
import subprocess
import sys
import time

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

user32.RegisterHotKey.argtypes = [wt.HWND, ctypes.c_int, wt.UINT, wt.UINT]
user32.RegisterHotKey.restype = wt.BOOL
user32.UnregisterHotKey.argtypes = [wt.HWND, ctypes.c_int]
user32.UnregisterHotKey.restype = wt.BOOL
kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.OpenProcess.restype = wt.HANDLE
kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.TerminateProcess.argtypes = [wt.HANDLE, wt.UINT]
kernel32.TerminateProcess.restype = wt.BOOL

MOD_CTRL, MOD_ALT = 0x0002, 0x0001
MOD = MOD_CTRL | MOD_ALT
MOD_NO_REPEAT = 0x4000
PROCESS_TERMINATE = 0x0001
VK_F9, VK_F10 = 0x78, 0x79
PROBE_ID = 0xBEEF


def probe(mods, vk):
    ctypes.set_last_error(0)
    if user32.RegisterHotKey(None, PROBE_ID, mods, vk):
        user32.UnregisterHotKey(None, PROBE_ID)
        return "free"
    return "taken" if ctypes.get_last_error() == 1409 else "error"


def child_main(vk):
    ctypes.set_last_error(0)
    if not user32.RegisterHotKey(None, PROBE_ID, MOD | MOD_NO_REPEAT, vk):
        print(f"CHILD_FAIL:{ctypes.get_last_error()}", flush=True)
        sys.exit(2)
    print(f"CHILD_READY:{vk}", flush=True)
    while True:
        time.sleep(0.3)


def spawn(vk, label):
    p = subprocess.Popen([sys.executable, __file__, "child", hex(vk)],
                         stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    for _ in range(80):
        line = p.stdout.readline()
        if line.startswith("CHILD_READY"):
            print(f"  启动 {label}  (PID {p.pid}, 注册 {'F9' if vk == VK_F9 else 'F10'})")
            return p
        if line.startswith("CHILD_FAIL"):
            print(f"  !! {label} 启动失败: {line.strip()}")
            p.kill()
            return None
    p.kill()
    return None


def kill(p, label):
    h = kernel32.OpenProcess(PROCESS_TERMINATE, False, p.pid)
    if h:
        kernel32.TerminateProcess(h, 1)
        kernel32.CloseHandle(h)
    p.wait(timeout=5)
    time.sleep(0.3)
    print(f"  结束 {label}  (PID {p.pid})")


def snap():
    return {"F9": probe(MOD, VK_F9), "F10": probe(MOD, VK_F10)}


def show(tag):
    s = snap()
    print(f"  {tag:<28} F9={s['F9']:<6} F10={s['F10']}")
    return s


def run():
    print()
    print("=" * 70)
    print("  实验：能否用「遍历进程 + 探测」建立热键归属映射")
    print("        （零注入、零驱动、零知识库）")
    print("=" * 70)

    base = show("阶段0 基线")
    if base["F9"] != "free" or base["F10"] != "free":
        print("  !! 基线不是 free，请换键后重试")
        return

    print()
    a = spawn(VK_F9, "进程A")
    b = spawn(VK_F10, "进程B")
    if not a or not b:
        return

    before = show("阶段1 两者注册后")
    print(f"       ^ 只知道两键被占，不知道分别是谁 —— 这正是系统不告诉我们的事")

    print()
    kill(a, "进程A")
    after_a = show("阶段2 结束A后")
    ok_a = after_a["F9"] == "free" and after_a["F10"] == "taken"
    print(f"       ^ 归属确立：F9 属于 A，F10 仍被占")
    print(f"       {'✓ 命中' if ok_a else '✗ 未按预期变化'}")

    print()
    print("  重启 A，验证「恢复」是否让热键重新被注册：")
    a2 = spawn(VK_F9, "进程A(恢复)")
    if a2:
        restored = show("阶段3 恢复A后")
        ok_r = restored["F9"] == "taken"
        print(f"       {'✓ 恢复成功，热键重新被注册' if ok_r else '✗ 恢复后热键未重新注册'}")

    print()
    kill(b, "进程B")
    after_b = show("阶段4 结束B后")
    ok_b = after_b["F10"] == "free"
    print(f"       {'✓ F10 释放，B 的归属确立' if ok_b else '✗ 未按预期变化'}")

    if a2:
        a2.kill()
        a2.wait(timeout=5)

    print()
    print("-" * 70)
    print("  结论：")
    print(f"    · 探测可判定「是否被占」                        {'成立' if before['F9'] == 'taken' else '不成立'}")
    print(f"    · 结束进程 + 重探测可确立「某键属于某进程」      {'成立' if ok_a and ok_b else '不成立'}")
    print(f"    · 重启进程后热键自动重新注册，恢复闭环可用       {'成立' if a2 and restored['F9'] == 'taken' else '不成立'}")
    print()
    print("  → 意味着：遍历所有候选进程（逐个结束+探测），")
    print("    可以在零注入、零驱动、零知识库的前提下，")
    print("    构建出完整的「进程 -> 热键集合」映射表。")
    print("=" * 70)
    print()


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "child":
        child_main(int(sys.argv[2], 16))
    else:
        run()
