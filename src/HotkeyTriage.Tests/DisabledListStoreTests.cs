using HotkeyTriage.Core.Act;
using Xunit;

namespace HotkeyTriage.Tests;

public class DisabledListStoreTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "hktriage_test_" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void Save_and_list_unrestored()
    {
        var dir = TempDir();
        try
        {
            var store = new DisabledListStore(dir);
            var snap = new DisabledSnapshot(
                Id: "test-001",
                DisabledAt: DateTime.Now,
                Restored: false,
                Pid: 1234,
                ExeName: "weixin.exe",
                FullPath: @"F:\WeChat\Weixin.exe",
                Args: null, WorkingDir: null,
                Reason: "release_hotkeys");
            store.Save(snap);

            var list = store.ListUnrestored();
            Assert.Single(list);
            Assert.Equal("test-001", list[0].Id);
            Assert.Equal("weixin.exe", list[0].ExeName);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void MarkRestored_removes_from_unrestored_list()
    {
        var dir = TempDir();
        try
        {
            var store = new DisabledListStore(dir);
            store.Save(new DisabledSnapshot("test-002", DateTime.Now, false,
                1234, "foo.exe", @"C:\foo.exe", null, null, "test"));

            Assert.Single(store.ListUnrestored());
            store.MarkRestored("test-002");
            Assert.Empty(store.ListUnrestored());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Corrupted_file_does_not_break_listing()
    {
        var dir = TempDir();
        try
        {
            var store = new DisabledListStore(dir);
            store.Save(new DisabledSnapshot("good", DateTime.Now, false,
                1, "a.exe", @"C:\a.exe", null, null, "x"));
            // 写一个损坏的 JSON
            File.WriteAllText(Path.Combine(dir, "bad.json"), "{ not valid json");

            var list = store.ListUnrestored();
            Assert.Single(list); // 只返回好的那条，损坏的被跳过
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
