using HotkeyTriage.Core.Attribute;
using HotkeyTriage.Core.Probe;
using Xunit;

namespace HotkeyTriage.Tests;

public class ProcessClassifierTests
{
    [Fact]
    public void Process_in_windows_dir_is_system()
    {
        var info = new ProcInfo(1, "explorer.exe", @"C:\Windows\explorer.exe", true);
        Assert.True(ProcessClassifier.IsSystemProcess(info));
    }

    [Fact]
    public void Process_in_system32_is_system()
    {
        var info = new ProcInfo(2, "svchost.exe", @"C:\Windows\System32\svchost.exe", false);
        Assert.True(ProcessClassifier.IsSystemProcess(info));
    }

    [Fact]
    public void Process_in_user_dir_is_not_system()
    {
        var info = new ProcInfo(3, "weixin.exe", @"F:\SoftWare\WeChat\Weixin\Weixin.exe", true);
        Assert.False(ProcessClassifier.IsSystemProcess(info));
    }

    [Fact]
    public void Process_in_program_files_is_not_system()
    {
        var info = new ProcInfo(4, "foo.exe", @"C:\Program Files\Foo\foo.exe", true);
        Assert.False(ProcessClassifier.IsSystemProcess(info));
    }

    [Fact]
    public void Process_with_empty_path_is_not_system()
    {
        var info = new ProcInfo(5, "unknown", "", false);
        Assert.False(ProcessClassifier.IsSystemProcess(info));
    }

    [Fact]
    public void Classification_is_case_insensitive()
    {
        var info = new ProcInfo(6, "x.exe", @"c:\windows\X.exe", false);
        Assert.True(ProcessClassifier.IsSystemProcess(info));
    }
}
