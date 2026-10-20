using System.Diagnostics;
using System.Text;
using HotkeyTriage.Core.Interop;
using HotkeyTriage.Core.Probe;

namespace HotkeyTriage.Core.Attribute;

/// <summary>一个进程的"身份信息"：它是什么程序、谁做的、当前窗口叫什么。</summary>
public sealed record AppInfo(
    string ExeName,
    string Company,
    string Product,
    string WindowTitle,
    string Version = "")
{
    /// <summary>厂商标识：优先公司名，缺失时退到产品名。</summary>
    public string Vendor =>
        !string.IsNullOrWhiteSpace(Company) ? Company
        : !string.IsNullOrWhiteSpace(Product) ? Product
        : "";

    /// <summary>紧凑身份串，如 <c>Weixin.exe · Tencent</c>。</summary>
    public string Identity
    {
        get
        {
            var who = Vendor;
            return string.IsNullOrWhiteSpace(who) ? ExeName : $"{ExeName} · {who}";
        }
    }

    public static AppInfo Empty(string exeName = "") => new(exeName, "", "", "");
}

public static class AppInfoProvider
{
    /// <summary>
    /// 从 exe 路径读公司/产品名，从窗口读标题。
    /// 任何失败都降级为空字段 —— 这只是展示用的补充信息，不允许影响主流程。
    /// </summary>
    public static AppInfo For(ProcInfo p)
    {
        string company = "", product = "", version = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(p.FullPath) && File.Exists(p.FullPath))
            {
                var vi = FileVersionInfo.GetVersionInfo(p.FullPath);
                company = (vi.CompanyName ?? "").Trim();
                product = (vi.ProductName ?? vi.FileDescription ?? "").Trim();
                version = (vi.FileVersion ?? "").Trim();
            }
        }
        catch { /* 忽略：文件被占用 / 无权限 / 无版本信息 */ }

        return new AppInfo(p.ExeName, company, product, GetWindowTitle(p.Pid), version);
    }

    /// <summary>取该进程第一个可见且有标题的顶层窗口标题。</summary>
    public static string GetWindowTitle(int pid)
    {
        string title = "";
        try
        {
            NativeMethods.EnumWindows((h, _) =>
            {
                if (!NativeMethods.IsWindowVisible(h)) return true;
                NativeMethods.GetWindowThreadProcessId(h, out uint winPid);
                if (winPid != (uint)pid) return true;
                int len = NativeMethods.GetWindowTextLengthW(h);
                if (len <= 0) return true;
                var sb = new StringBuilder(len + 2);
                NativeMethods.GetWindowTextW(h, sb, sb.Capacity);
                title = sb.ToString();
                return false;   // 找到第一个即可
            }, IntPtr.Zero);
        }
        catch { /* 忽略 */ }
        return title;
    }
}
