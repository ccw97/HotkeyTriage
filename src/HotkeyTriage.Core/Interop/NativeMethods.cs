using System.Runtime.InteropServices;

namespace HotkeyTriage.Core.Interop;

/// <summary>
/// Win32 P/Invoke 声明集中管理。所有原生调用必须经此封装，不得散落。
/// </summary>
public static class NativeMethods
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>热键已被注册（被他人占用）</summary>
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
