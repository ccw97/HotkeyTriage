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
        Application.Run(new MainForm());
    }
}
