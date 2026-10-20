using HotkeyTriage.Core.Probe;
using Xunit;

namespace HotkeyTriage.Tests;

/// <summary>
/// ProcessScanner 测试。
/// 这类测试依赖真实运行环境，属集成测试性质——但断言写成对环境的弱假设（>50、自身存在、至少一个有窗口），
/// 避免在特定机器上误红。
/// </summary>
public class ProcessScannerTests
{
    [Fact]
    public void Scan_returns_more_than_50_processes()
    {
        var scanner = new ProcessScanner();
        var procs = scanner.Scan();
        Assert.True(procs.Count > 50, $"只扫到 {procs.Count} 个进程，环境异常或扫描失败");
    }

    [Fact]
    public void Scan_includes_current_process_with_path()
    {
        var scanner = new ProcessScanner();
        var procs = scanner.Scan();
        var self = procs.FirstOrDefault(p => p.Pid == Environment.ProcessId);
        Assert.NotNull(self);
        Assert.False(string.IsNullOrEmpty(self!.FullPath),
            "拿不到自身进程路径——PROCESS_QUERY_LIMITED_INFORMATION 权限不足或实现有误");
    }

    [Fact]
    public void FullPath_is_complete_and_exists()
    {
        // 回归用例：曾因 QueryFullProcessImageNameW 少了 CharSet.Unicode，
        // UTF-16 被按 ANSI 解码，路径被截断成第一个字符（"F:\..." -> "F"）。
        // 只断言"非空"抓不到，必须断言路径真实存在。
        var scanner = new ProcessScanner();
        var self = scanner.Scan().First(p => p.Pid == Environment.ProcessId);

        Assert.True(self.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
            $"路径不完整: [{self.FullPath}]");
        Assert.True(File.Exists(self.FullPath), $"路径指向的文件不存在: [{self.FullPath}]");
    }

    [Fact]
    public void FullPath_of_other_processes_is_not_truncated()
    {
        // 抽一个系统进程验证跨进程读取：路径要么为空（受保护），要么是完整路径
        var scanner = new ProcessScanner();
        var sample = scanner.Scan()
            .Where(p => p.Pid > 4 && !string.IsNullOrEmpty(p.FullPath))
            .Take(20)
            .ToList();

        Assert.NotEmpty(sample);
        foreach (var p in sample)
        {
            Assert.True(p.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        || p.FullPath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase),
                $"{p.ExeName} 的路径可疑: [{p.FullPath}]");
        }
    }

    [Fact]
    public void Scan_does_not_throw_for_system_process()
    {
        var scanner = new ProcessScanner();
        var procs = scanner.Scan();
        // System 进程 PID 4 通常存在；路径为空属正常，但枚举不应抛异常
        var sys = procs.FirstOrDefault(p => p.Pid == 4);
        Assert.NotNull(sys);
    }

    [Fact]
    public void Scan_finds_at_least_one_visible_window_process()
    {
        var scanner = new ProcessScanner();
        var procs = scanner.Scan();
        Assert.Contains(procs, p => p.HasVisibleWindow);
    }

    [Fact]
    public void Scan_exenames_are_lowercase()
    {
        var scanner = new ProcessScanner();
        var procs = scanner.Scan();
        foreach (var p in procs)
        {
            Assert.Equal(p.ExeName, p.ExeName.ToLowerInvariant());
        }
    }
}
