using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Attribute;

/// <summary>
/// 已知会安装「低级键盘钩子」的常见软件。
///
/// 为什么需要这个：用 WH_KEYBOARD_LL 吞键的程序**不注册热键**，因此
///   - 探测看不到它（热键表里没有它的项）
///   - 快速识别看不到它（不会有 WM_HOTKEY 投递）
///   - 深扫也归不了因（结束进程前后探测结果都是 FREE）
/// 即：这类占用者在用户态没有任何可反查的接口。
///
/// 所以这里只做「可疑对象提示」，不下结论：把正在运行的已知钩子型软件列出来，
/// 让用户自己去逐个退出验证。宁可说"可能是它"，也不假装确定。
/// </summary>
public static class HookSuspects
{
    private static readonly (string Exe, string Name)[] Known =
    {
        ("autohotkey.exe",    "AutoHotkey 脚本"),
        ("autohotkeyu64.exe", "AutoHotkey 脚本"),
        ("autohotkeyu32.exe", "AutoHotkey 脚本"),
        ("obs64.exe",         "OBS 录屏"),
        ("obs32.exe",         "OBS 录屏"),
        ("snipaste.exe",      "Snipaste 截图"),
        ("sharex.exe",        "ShareX 截图"),
        ("bandicam.exe",      "Bandicam 录屏"),
        ("ctfmon.exe",        "输入法框架"),
        ("chsime.exe",        "微软拼音"),
        ("sogouinput.exe",    "搜狗输入法"),
        ("qqpinyin.exe",      "QQ 输入法"),
        ("quicker.exe",       "Quicker 快捷面板"),
        ("uTools.exe",        "uTools 快捷面板"),
    };

    /// <summary>返回正在运行的已知钩子型软件（去重，形如「AutoHotkey 脚本 (autohotkey.exe)」）。</summary>
    public static IReadOnlyList<string> Running(IEnumerable<ProcInfo> procs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var p in procs)
        {
            foreach (var (exe, name) in Known)
            {
                if (!string.Equals(exe, p.ExeName, StringComparison.OrdinalIgnoreCase)) continue;
                var label = $"{name} ({p.ExeName})";
                if (seen.Add(label)) result.Add(label);
            }
        }
        return result;
    }
}
