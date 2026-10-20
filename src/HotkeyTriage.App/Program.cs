namespace HotkeyTriage.App;

/// <summary>
/// App 入口。当前为骨架，UI 在后续任务实现。
/// v3.2：manifest 要求管理员权限，双击启动会弹 UAC。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        // M1 阶段先不实现 UI，只占位
        System.Windows.Forms.MessageBox.Show(
            "HotkeyTriage 骨架已启动（管理员权限已获取）。\n" +
            "UI 待 M5 实现。",
            "HotkeyTriage",
            System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Information);
    }
}
