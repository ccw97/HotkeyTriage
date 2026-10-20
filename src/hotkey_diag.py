# -*- coding: utf-8 -*-
"""
HotkeyTriage · 核心诊断引擎（P0 参照实现）
==========================================
职责：
  1. Probe      —— 用 RegisterHotKey 试注册，判定组合键在系统层面是否被占用
  2. ProcessMap —— 枚举运行中进程，拿到 exe 名与完整路径
  3. KnowledgeBase —— 进程签名 -> 已知全局热键 / 处置方式
  4. Attribute  —— 归因：把"被占用"收敛到"谁占的、什么机理、怎么解"
  5. Report     —— 输出人话结论

设计要点（与方案文档一致）：
  · 零注入、零驱动、纯用户态，因此对反作弊安全
  · 探测只回答"是否被占"，"是谁"必须靠归因层推断，不能枚举
  · 系统壳热键（Win+*）与系统保留键必须从结果中剥离，否则 92% 是噪声

用法：
  python hotkey_diag.py scan              # 全量扫描 + 归因
  python hotkey_diag.py check Ctrl+Alt+A  # 单键诊断
  python hotkey_diag.py procs             # 列出运行中的已知抢键软件
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import json
import sys
from pathlib import Path

if sys.stdout.encoding and sys.stdout.encoding.lower() not in ("utf-8", "utf8"):
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

# ---------------------------------------------------------------- Win32 绑定
user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

user32.RegisterHotKey.argtypes = [wt.HWND, ctypes.c_int, wt.UINT, wt.UINT]
user32.RegisterHotKey.restype = wt.BOOL
user32.UnregisterHotKey.argtypes = [wt.HWND, ctypes.c_int]
user32.UnregisterHotKey.restype = wt.BOOL

kernel32.CreateToolhelp32Snapshot.argtypes = [wt.DWORD, wt.DWORD]
kernel32.CreateToolhelp32Snapshot.restype = wt.HANDLE
kernel32.Process32FirstW.argtypes = [wt.HANDLE, ctypes.c_void_p]
kernel32.Process32FirstW.restype = wt.BOOL
kernel32.Process32NextW.argtypes = [wt.HANDLE, ctypes.c_void_p]
kernel32.Process32NextW.restype = wt.BOOL
kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.OpenProcess.restype = wt.HANDLE
kernel32.QueryFullProcessImageNameW.argtypes = [
    wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
kernel32.QueryFullProcessImageNameW.restype = wt.BOOL
kernel32.CloseHandle.argtypes = [wt.HANDLE]

TH32CS_SNAPPROCESS = 0x00000002
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

MOD_ALT, MOD_CTRL, MOD_SHIFT, MOD_WIN = 0x0001, 0x0002, 0x0004, 0x0008
ERR_HOTKEY_ALREADY_REGISTERED = 1409
PROBE_ID = 0xBEEF


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wt.DWORD),
        ("cntUsage", wt.DWORD),
        ("th32ProcessID", wt.DWORD),
        ("th32DefaultHeapID", ctypes.c_size_t),
        ("th32ModuleID", wt.DWORD),
        ("cntThreads", wt.DWORD),
        ("th32ParentProcessID", wt.DWORD),
        ("pcPriClassBase", ctypes.c_long),
        ("dwFlags", wt.DWORD),
        ("szExeFile", wt.WCHAR * 260),
    ]


# ---------------------------------------------------------------- 键名表
VK_TO_NAME: dict[int, str] = {}
for _c in range(0x41, 0x5B):
    VK_TO_NAME[_c] = chr(_c)
for _c in range(0x30, 0x3A):
    VK_TO_NAME[_c] = chr(_c)
for _i in range(24):
    VK_TO_NAME[0x70 + _i] = f"F{_i + 1}"
VK_TO_NAME.update({
    0x08: "Backspace", 0x09: "Tab", 0x0D: "Enter", 0x1B: "Esc", 0x20: "Space",
    0x21: "PgUp", 0x22: "PgDn", 0x23: "End", 0x24: "Home",
    0x25: "Left", 0x26: "Up", 0x27: "Right", 0x28: "Down",
    0x2C: "PrtSc", 0x2D: "Insert", 0x2E: "Delete", 0x5B: "LWin", 0x5C: "RWin",
    0xBA: ";", 0xBB: "=", 0xBC: ",", 0xBD: "-", 0xBE: ".", 0xBF: "/",
    0xC0: "`", 0xDB: "[", 0xDC: "\\", 0xDD: "]", 0xDE: "'",
})
NAME_TO_VK = {v: k for k, v in VK_TO_NAME.items()}

MOD_TO_LABEL = [(MOD_CTRL, "Ctrl"), (MOD_ALT, "Alt"), (MOD_SHIFT, "Shift"), (MOD_WIN, "Win")]

# 系统壳 / 系统保留热键：用户不该也无法处置
SYSTEM_RESERVED_RULES = {
    (MOD_ALT, 0x09): "Alt+Tab 任务切换",
    (MOD_ALT, 0x73): "Alt+F4 关闭窗口",
    (MOD_CTRL | MOD_ALT, 0x2E): "Ctrl+Alt+Del 安全注意序列",
    (MOD_CTRL | MOD_SHIFT, 0x1B): "Ctrl+Shift+Esc 任务管理器",
    (MOD_CTRL, 0x1B): "Ctrl+Esc 开始菜单",
    (MOD_ALT | MOD_SHIFT, 0x09): "Alt+Shift+Tab 反向切换",
    (MOD_CTRL | MOD_ALT, 0x09): "Ctrl+Alt+Tab 任务视图",
    (MOD_CTRL | MOD_ALT | MOD_SHIFT, 0x09): "Ctrl+Alt+Shift+Tab 任务视图",
    (MOD_WIN, 0x09): "Win+Tab 任务视图",
    (MOD_WIN, 0x20): "Win+Space 输入法切换",
    (MOD_WIN, 0x4C): "Win+L 锁屏",
    (MOD_WIN, 0x44): "Win+D 显示桌面",
    (MOD_WIN, 0x45): "Win+E 文件资源管理器",
    (MOD_WIN, 0x52): "Win+R 运行",
    (MOD_WIN, 0x4B): "Win+K 连接设备",
    (MOD_WIN, 0x41): "Win+A 快速设置",
    (MOD_WIN, 0x49): "Win+I 系统设置",
    (MOD_WIN, 0x58): "Win+X 高级用户菜单",
}
# 可以被用户关闭的系统功能（属于"系统但可处置"）
SYSTEM_DISABLEABLE = {
    "Win+G": ("Xbox Game Bar 录制栏", "设置 → 游戏 → Xbox Game Bar → 关"),
    "Win+V": ("剪贴板历史", "设置 → 系统 → 剪贴板 → 关闭历史"),
    "Win+H": ("语音输入", "设置 → 辅助功能 → 语音 → 关闭"),
    "Win+Shift+S": ("截图工具", "设置 → 辅助功能 → 键盘 → 截图快捷键"),
    "Win+P": ("投影/多显示器", "设置 → 系统 → 显示 → 投影"),
}


# ---------------------------------------------------------------- 组合键工具
def combo_str(mods: int, vk: int) -> str:
    parts = [label for m, label in MOD_TO_LABEL if mods & m]
    parts.append(VK_TO_NAME.get(vk, f"0x{vk:02X}"))
    return "+".join(parts)


def parse_combo(text: str) -> tuple[int, int]:
    mods = 0
    vk = 0
    for token in text.replace(" ", "").split("+"):
        t = token.lower()
        if t in ("ctrl", "control"):
            mods |= MOD_CTRL
        elif t == "alt":
            mods |= MOD_ALT
        elif t == "shift":
            mods |= MOD_SHIFT
        elif t in ("win", "super", "meta"):
            mods |= MOD_WIN
        else:
            key = token.upper()
            if key not in NAME_TO_VK:
                raise ValueError(f"无法识别的按键: {token}")
            vk = NAME_TO_VK[key]
    if not vk:
        raise ValueError("缺少主键")
    return mods, vk


# ---------------------------------------------------------------- Probe 层
def probe_hotkey(mods: int, vk: int, hotkey_id: int = PROBE_ID) -> tuple[str, int]:
    """试注册探测。返回 ('free'|'taken'|'error', 错误码)"""
    ctypes.set_last_error(0)
    if user32.RegisterHotKey(None, hotkey_id, mods, vk):
        user32.UnregisterHotKey(None, hotkey_id)
        return "free", 0
    return "taken", ctypes.get_last_error()


def build_probe_grid() -> list[tuple[int, int]]:
    """候选热键网格：覆盖游戏与日常软件最易冲突的区域"""
    vks = list(range(0x41, 0x5B)) + list(range(0x30, 0x3A)) + list(range(0x70, 0x7C))
    vks += [0x09, 0x1B, 0x20, 0x21, 0x22, 0x23, 0x24, 0x2D, 0x2E,
            0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE]
    mod_sets = [
        0, MOD_CTRL, MOD_ALT, MOD_SHIFT, MOD_WIN,
        MOD_CTRL | MOD_ALT, MOD_CTRL | MOD_SHIFT, MOD_ALT | MOD_SHIFT,
        MOD_CTRL | MOD_WIN, MOD_ALT | MOD_WIN, MOD_SHIFT | MOD_WIN,
        MOD_CTRL | MOD_ALT | MOD_SHIFT, MOD_CTRL | MOD_ALT | MOD_WIN,
    ]
    grid = []
    for mods in mod_sets:
        for vk in vks:
            if mods == 0 and not (0x70 <= vk <= 0x7B):
                continue  # 裸字母/数字键不属于全局热键范畴
            grid.append((mods, vk))
    return grid


# ---------------------------------------------------------------- 进程层
def enum_processes() -> dict[int, dict]:
    """返回 {pid: {'exe': 小写文件名, 'path': 完整路径}}"""
    result: dict[int, dict] = {}
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if not snap or snap == INVALID_HANDLE_VALUE:
        return result
    entry = PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(PROCESSENTRY32W)
    try:
        ok = kernel32.Process32FirstW(snap, ctypes.byref(entry))
        while ok:
            pid = entry.th32ProcessID
            exe = entry.szExeFile.lower()
            path = ""
            h = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
            if h:
                buf = ctypes.create_unicode_buffer(1024)
                size = wt.DWORD(1024)
                if kernel32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(size)):
                    path = buf.value
                kernel32.CloseHandle(h)
            result[pid] = {"exe": exe, "path": path}
            ok = kernel32.Process32NextW(snap, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snap)
    return result


# ---------------------------------------------------------------- 知识库
class KnowledgeBase:
    def __init__(self, path: Path):
        raw = json.loads(path.read_text(encoding="utf-8"))
        self.items = {k: v for k, v in raw.items() if not k.startswith("_")}
        self.by_combo: dict[str, list[str]] = {}
        for exe, info in self.items.items():
            for hk in info.get("hotkeys", []):
                self.by_combo.setdefault(hk.upper(), []).append(exe)

    def lookup(self, combo: str) -> list[dict]:
        """按组合键反查：哪些已知软件默认声明了它"""
        return [{"exe": exe, **self.items[exe]}
                for exe in self.by_combo.get(combo.upper(), [])]


# ---------------------------------------------------------------- 归因层
def classify(mods: int, vk: int, state: str, kb: KnowledgeBase,
             procs: dict[int, dict]) -> dict:
    """把一条探测结果判定为：system / disableable_system / third_party / unknown"""
    combo = combo_str(mods, vk)

    if (mods, vk) in SYSTEM_RESERVED_RULES:
        return {"kind": "system", "combo": combo, "reason": SYSTEM_RESERVED_RULES[(mods, vk)]}
    if combo in SYSTEM_DISABLEABLE:
        name, hint = SYSTEM_DISABLEABLE[combo]
        return {"kind": "system_disableable", "combo": combo, "reason": name, "hint": hint}

    candidates = kb.lookup(combo)
    if candidates:
        running = [(pid, p) for pid, p in procs.items()
                   if p["exe"] in {c["exe"] for c in candidates}]
        if running:
            top = candidates[0]
            owner_pid, owner = running[0]
            return {
                "kind": "third_party",
                "combo": combo,
                "owner": owner["path"] or owner["exe"],
                "pid": owner_pid,
                "vendor": top.get("vendor", "?"),
                "confidence": 0.90,
                "mechanism": "register",
                "disable_hint": top.get("disable_hint", ""),
                "safe_to_kill": top.get("safe_to_kill", False),
                "evidence": "knowledge_base",
            }
        return {"kind": "known_but_idle", "combo": combo,
                "reason": f"知识库匹配到 {candidates[0]['vendor']}，但该进程未运行"}

    if mods & MOD_WIN:
        return {"kind": "system", "combo": combo, "reason": "Windows 系统壳热键（Explorer/DWM 注册）"}

    return {"kind": "unknown", "combo": combo,
            "reason": "已注册但无法归因（无对应知识库条目）"}


def find_hook_suspects(procs: dict[int, dict]) -> list[dict]:
    """钩子型占用者：它们不占热键表，探测不到，但常是真凶"""
    suspects = []
    for pid, p in procs.items():
        info = KB_REF.items.get(p["exe"]) if KB_REF else None
        if info and info.get("hotkeys") == ["*"]:
            suspects.append({"pid": pid, "exe": p["exe"], "path": p["path"],
                             "vendor": info.get("vendor", "")})
    return suspects


KB_REF: KnowledgeBase | None = None


# ---------------------------------------------------------------- 报告
C = {
    "reset": "\033[0m", "dim": "\033[2m", "b": "\033[1m",
    "red": "\033[31m", "green": "\033[32m", "yellow": "\033[33m",
    "blue": "\033[36m",
}


def _c(text: str, color: str) -> str:
    return f"{C.get(color, '')}{text}{C['reset']}"


def cmd_scan(verbose: bool = False):
    procs = enum_processes()
    grid = build_probe_grid()

    taken = []
    for mods, vk in grid:
        state, _ = probe_hotkey(mods, vk)
        if state == "taken":
            taken.append(classify(mods, vk, state, KB_REF, procs))

    buckets: dict[str, list[dict]] = {}
    for item in taken:
        buckets.setdefault(item["kind"], []).append(item)

    print()
    print(_c("  HotkeyTriage  全局热键冲突诊断", "b"))
    print(_c(f"  扫描 {len(grid)} 个组合 · 命中 {len(taken)} 个", "dim"))
    print("  " + "─" * 58)

    third = buckets.get("third_party", [])
    if third:
        print()
        print(_c(f"  ● 第三方软件占用（{len(third)}）— 可直接处置", "yellow"))
        for it in third:
            print()
            print(f"    {_c(it['combo'], 'b')}  ←  {it['vendor']}  "
                  f"{_c(f'置信度 {it["confidence"]:.0%}', 'dim')}")
            print(f"      进程  {it['owner']}")
            print(f"      PID   {it['pid']}   机理  注册型（挂起无效，需结束或改设置）")
            if it["disable_hint"]:
                print(f"      改键  {_c(it['disable_hint'], 'green')}  ← 推荐")
            kill_note = "可直接结束并自动恢复" if it["safe_to_kill"] else "结束时可能丢数据，慎用"
            print(f"      结束  {kill_note}")

    dis = buckets.get("system_disableable", [])
    if dis:
        print()
        print(_c(f"  ● 可关闭的系统功能（{len(dis)}）", "blue"))
        for it in dis:
            print(f"    {it['combo']:14s} {it['reason']}   {_c(it['hint'], 'dim')}")

    unk = buckets.get("unknown", [])
    if unk:
        print()
        print(_c(f"  ● 已占用但无法归因（{len(unk)}）— 需用二分排除法定位", "red"))
        for it in unk:
            print(f"    {it['combo']}")

    hooks = find_hook_suspects(procs)
    if hooks:
        print()
        print(_c(f"  ● 钩子型风险进程（{len(hooks)}）— 探测不到，但可能正是元凶", "red"))
        for h in hooks:
            print(f"    {h['exe']:<24s} PID {h['pid']:<6d} {_c(h['path'], 'dim')}")

    sys_n = len(buckets.get("system", [])) + len(buckets.get("known_but_idle", []))
    print()
    print("  " + "─" * 58)
    print(_c(f"  系统保留/壳热键 {sys_n} 个已过滤（Win+*、Alt+Tab 等，不建议处置）", "dim"))
    actionable = len(third) + len(dis)
    print(f"  对你真正有用的结论：{_c(str(actionable) if actionable else '0', 'green')} 条"
          f"  （原始命中 {len(taken)} 条，噪声 {len(taken) - actionable} 条）")
    print()


def cmd_check(combo: str):
    procs = enum_processes()
    mods, vk = parse_combo(combo)
    state, err = probe_hotkey(mods, vk)
    print()
    print(f"  诊断目标：{_c(combo_str(mods, vk), 'b')}")
    print("  " + "─" * 58)

    if state == "free":
        print(f"  探测结果：{_c('未被占用', 'green')}  （RegisterHotKey 注册成功）")
        print()
        print("  判定：系统层面不存在注册型冲突。")
        print("  那么按键失灵的原因可能是：")
        print("    1. 低级键盘钩子型占用（AHK / 录屏 / 宏工具）—— 探测看不到")
        print("    2. 输入法状态截获（中文输入法激活时）")
        print("    3. 游戏内键位未绑定，或使用 Raw Input 独占")
        print(f"  {'':4s}→ 建议先切英文输入法再试；仍无效则用二分排除法")
        print()
        return

    item = classify(mods, vk, state, KB_REF, procs)
    kind = item["kind"]
    print(f"  探测结果：{_c('已被占用', 'red')}  （错误码 {err} = 热键已注册）")
    print()

    if kind == "third_party":
        print(f"  占用者：{_c(item['vendor'], 'b')}")
        print(f"  路径  ：{item['owner']}")
        print(f"  PID   ：{item['pid']}")
        print(f"  机理  ：注册型 —— 挂起进程无效，只能结束或让它自己注销")
        print()
        print(f"  推荐：{_c(item['disable_hint'], 'green')}")
        print(f"  备选：结束该进程（{'安全' if item['safe_to_kill'] else '可能丢数据'}）")
    elif kind == "system_disableable":
        print(f"  占用者：Windows 自带功能 — {item['reason']}")
        print(f"  关闭  ：{item['hint']}")
    elif kind == "system":
        print(f"  占用者：{_c('系统保留热键', 'blue')} — {item['reason']}")
        print("  结论  ：这是 Windows 自身功能，不建议也无法关闭，请换键或绕行重映射")
    else:
        print("  占用者：未知（无知识库匹配）")
        print("  下一步：使用二分排除 —— 按组结束用户态进程，每轮复测本键")
    print()


def cmd_procs():
    procs = enum_processes()
    known = [(pid, p, KB_REF.items[p["exe"]])
             for pid, p in procs.items() if p["exe"] in KB_REF.items]
    print()
    print(f"  运行中的已知抢键软件（{len(known)}）")
    print("  " + "─" * 58)
    for pid, p, info in sorted(known, key=lambda x: x[2].get("vendor", "")):
        hk = ", ".join(info["hotkeys"]) or "无全局热键"
        print(f"  {p['exe']:<22s} {info.get('vendor', ''):<10s} {hk}")
    print()


def main():
    global KB_REF
    here = Path(__file__).resolve().parent
    kb_path = here / "hotkey_kb.json"
    if not kb_path.exists():
        print(f"缺少知识库: {kb_path}")
        sys.exit(1)
    KB_REF = KnowledgeBase(kb_path)

    action = sys.argv[1] if len(sys.argv) > 1 else "scan"
    if action == "scan":
        cmd_scan()
    elif action == "check" and len(sys.argv) > 2:
        cmd_check(sys.argv[2])
    elif action == "procs":
        cmd_procs()
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
