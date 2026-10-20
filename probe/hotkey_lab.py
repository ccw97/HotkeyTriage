# -*- coding: utf-8 -*-
"""
Windows 全局热键机制实证实验
目的：验证 3 个决定方案走向的技术事实
  1. RegisterHotKey 探测法能否判断某组合键是否被占用
  2. 挂起(Suspend)占用进程后，热键是否释放  <-- 最关键
  3. 结束进程后，热键是否释放

用法：
  python hotkey_lab.py child        # 内部用：注册热键并长驻
  python hotkey_lab.py experiment   # 跑对照实验
  python hotkey_lab.py snapshot     # 对本机做全量热键占用快照
"""
import ctypes
import ctypes.wintypes as wt
import json
import subprocess
import sys
import time

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
ntdll = ctypes.WinDLL("ntdll")

user32.RegisterHotKey.argtypes = [wt.HWND, ctypes.c_int, wt.UINT, wt.UINT]
user32.RegisterHotKey.restype = wt.BOOL
user32.UnregisterHotKey.argtypes = [wt.HWND, ctypes.c_int]
user32.UnregisterHotKey.restype = wt.BOOL

kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.OpenProcess.restype = wt.HANDLE
kernel32.CloseHandle.argtypes = [wt.HANDLE]
ntdll.NtSuspendProcess.argtypes = [wt.HANDLE]
ntdll.NtSuspendProcess.restype = ctypes.c_long
ntdll.NtResumeProcess.argtypes = [wt.HANDLE]
ntdll.NtResumeProcess.restype = ctypes.c_long

MOD_ALT, MOD_CTRL, MOD_SHIFT, MOD_WIN = 0x0001, 0x0002, 0x0004, 0x0008
MOD_NOREPEAT = 0x4000
ERR_HOTKEY_ALREADY_REGISTERED = 1409
PROCESS_SUSPEND_RESUME = 0x0800
PROCESS_TERMINATE = 0x0001

VK_F9 = 0x78
TEST_MOD = MOD_CTRL | MOD_ALT
TEST_VK = VK_F9
PROBE_ID = 0xBEEF

VK_NAMES = {
    0x08: "Backspace", 0x09: "Tab", 0x0D: "Enter", 0x1B: "Esc", 0x20: "Space",
    0x21: "PgUp", 0x22: "PgDn", 0x23: "End", 0x24: "Home",
    0x2D: "Insert", 0x2E: "Delete",
    0xBA: ";", 0xBB: "=", 0xBC: ",", 0xBD: "-", 0xBE: ".", 0xBF: "/",
    0xC0: "`", 0xDB: "[", 0xDC: "\\", 0xDD: "]", 0xDE: "'",
}
for _c in range(0x41, 0x5B):
    VK_NAMES[_c] = chr(_c)
for _c in range(0x30, 0x3A):
    VK_NAMES[_c] = chr(_c)
for _i in range(12):
    VK_NAMES[0x70 + _i] = f"F{_i + 1}"

MOD_LABELS = [
    (0, ""),
    (MOD_CTRL, "Ctrl+"),
    (MOD_ALT, "Alt+"),
    (MOD_SHIFT, "Shift+"),
    (MOD_WIN, "Win+"),
    (MOD_CTRL | MOD_ALT, "Ctrl+Alt+"),
    (MOD_CTRL | MOD_SHIFT, "Ctrl+Shift+"),
    (MOD_ALT | MOD_SHIFT, "Alt+Shift+"),
    (MOD_CTRL | MOD_WIN, "Ctrl+Win+"),
    (MOD_ALT | MOD_WIN, "Alt+Win+"),
    (MOD_SHIFT | MOD_WIN, "Shift+Win+"),
    (MOD_CTRL | MOD_ALT | MOD_SHIFT, "Ctrl+Alt+Shift+"),
    (MOD_CTRL | MOD_ALT | MOD_WIN, "Ctrl+Alt+Win+"),
]


def probe_once(mods, vk, hotkey_id=PROBE_ID):
    """探测一组热键：成功注册=空闲，1409=已被他人占用"""
    ctypes.set_last_error(0)
    if user32.RegisterHotKey(None, hotkey_id, mods, vk):
        user32.UnregisterHotKey(None, hotkey_id)
        return "free", 0
    return "taken", ctypes.get_last_error()


def child_main():
    """子进程：注册测试热键后长驻，不注销"""
    ctypes.set_last_error(0)
    ok = user32.RegisterHotKey(None, PROBE_ID, TEST_MOD | MOD_NOREPEAT, TEST_VK)
    if not ok:
        print(f"CHILD_REGISTER_FAILED:{ctypes.get_last_error()}", flush=True)
        sys.exit(2)
    print(f"CHILD_READY:{ctypes.windll.kernel32.GetCurrentProcessId()}", flush=True)
    while True:
        time.sleep(0.5)


def experiment():
    print("=" * 64)
    print("Windows 全局热键机制对照实验  Ctrl+Alt+F9")
    print("=" * 64)
    label = "Ctrl+Alt+F9"
    report = {}

    state, err = probe_once(TEST_MOD, TEST_VK)
    print(f"[阶段1] 干净基线       -> {state} (err={err})")
    report["baseline"] = state

    proc = subprocess.Popen(
        [sys.executable, __file__, "child"],
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
    )
    ready = False
    for _ in range(60):
        line = proc.stdout.readline()
        if line.startswith("CHILD_READY:"):
            ready = True
            print(f"[阶段2] 子进程已注册    -> PID {line.strip().split(':')[1]}")
            break
        if line.startswith("CHILD_REGISTER_FAILED"):
            print(f"       !! 子进程注册失败: {line.strip()}")
            proc.kill()
            return None
    if not ready:
        print("       !! 子进程未能就绪")
        proc.kill()
        return None

    time.sleep(0.3)
    state, err = probe_once(TEST_MOD, TEST_VK)
    print(f"[阶段3] 重探测同一组合  -> {state} (err={err})   <- 探测法有效性")
    report["after_child_register"] = state

    h = kernel32.OpenProcess(PROCESS_SUSPEND_RESUME, False, proc.pid)
    if not h:
        print("       !! OpenProcess 失败，无法挂起")
        proc.kill()
        return report
    rc = ntdll.NtSuspendProcess(h)
    time.sleep(0.3)
    alive = proc.poll() is None
    state, err = probe_once(TEST_MOD, TEST_VK)
    print(f"[阶段4] 挂起子进程      -> 进程存活={alive}, 热键={state} (err={err})")
    print(f"         ^ 关键：若仍为 taken，说明【挂起无法释放热键】")
    report["after_suspend"] = state
    report["suspend_kept_process_alive"] = alive

    ntdll.NtResumeProcess(h)
    kernel32.CloseHandle(h)
    time.sleep(0.2)

    proc.kill()
    proc.wait(timeout=5)
    time.sleep(0.4)
    state, err = probe_once(TEST_MOD, TEST_VK)
    print(f"[阶段5] 结束子进程      -> 热键={state} (err={err})")
    report["after_kill"] = state

    print("-" * 64)
    verdict = {
        "探测法可用": report.get("baseline") == "free" and report.get("after_child_register") == "taken",
        "挂起进程可释放热键": report.get("after_suspend") == "free",
        "结束进程可释放热键": report.get("after_kill") == "free",
    }
    for k, v in verdict.items():
        print(f"  {k}: {'是' if v else '否'}")
    report["verdict"] = verdict
    return report


def build_candidates():
    """构造候选热键集合：游戏与日常软件最容易冲突的区域"""
    keys = []
    vks = list(range(0x41, 0x5B))                      # A-Z
    vks += list(range(0x30, 0x3A))                     # 0-9
    vks += list(range(0x70, 0x7C))                     # F1-F12
    vks += [0x09, 0x1B, 0x20, 0x21, 0x22, 0x23, 0x24, 0x2D, 0x2E,
            0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE]
    for mods, _ in MOD_LABELS:
        for vk in vks:
            if mods == 0 and vk not in range(0x70, 0x7C):
                continue                                # 裸字母键不属全局热键范畴
            keys.append((mods, vk))
    return keys


def snapshot():
    """全量扫描本机已被占用的组合键"""
    keys = build_candidates()
    taken = []
    errs = {}
    for mods, vk in keys:
        state, err = probe_once(mods, vk)
        if state == "taken":
            label = next((l for m, l in MOD_LABELS if m == mods), "?")
            taken.append(label + VK_NAMES.get(vk, hex(vk)))
        elif state == "free":
            pass
        else:
            errs[err] = errs.get(err, 0) + 1
    out = {
        "scanned": len(keys),
        "taken_count": len(taken),
        "taken": taken,
        "error_codes": errs,
    }
    print(json.dumps(out, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    action = sys.argv[1] if len(sys.argv) > 1 else "experiment"
    if action == "child":
        child_main()
    elif action == "snapshot":
        snapshot()
    else:
        experiment()
