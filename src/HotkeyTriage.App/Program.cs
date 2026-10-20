using System.Drawing.Imaging;

namespace HotkeyTriage.App;

/// <summary>
/// App 入口。v3.2：manifest 要求管理员权限，双击启动会弹 UAC。
///
/// 调试用：`HotkeyTriage.exe --shot &lt;输出路径&gt;` 会把主窗口离屏渲染成 PNG 后退出，
/// 便于在没有交互桌面的环境下检查排版（不会弹窗抢焦点）。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length >= 2 && args[0] == "--shot")
        {
            RenderShot(args[1]);
            return;
        }

        Application.Run(new MainForm());
    }

    private static void RenderShot(string path)
    {
        using var f = new MainForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-6000, -6000),
            ShowInTaskbar = false,
        };
        f.Show();

        // 等快扫（Load 里 async）落地
        for (int i = 0; i < 80; i++)
        {
            Application.DoEvents();
            Thread.Sleep(60);
        }

        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        bmp.Save(path, ImageFormat.Png);
        f.Close();
        Console.WriteLine($"shot saved: {path} ({bmp.Width}x{bmp.Height})");
    }
}
