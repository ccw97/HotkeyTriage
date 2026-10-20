using HotkeyTriage.Core.Act;
using HotkeyTriage.Core.Discover;
using HotkeyTriage.Core.Probe;
using System.Diagnostics;
using Xunit;

namespace HotkeyTriage.Tests;

public class SingleLookupTests
{
    private const uint MOD = 0x0002 | 0x0001;

    private static ProcInfo Proc(int pid, string exe) =>
        new(pid, exe, $@"F:\fake\{exe}", false);

    [Fact]
    public void Free_key_returns_NoConflict()
    {
        var closed = new HashSet<int>();
        var probe = new FakeProbe(new(), closed);
        var lookup = new SingleLookup(probe, new FakeController(closed),
            new DisabledListStore(Path.Combine(Path.GetTempPath(), "hk_s_" + Guid.NewGuid().ToString("N")[..8])));

        var r = lookup.Lookup(MOD, 0x7A, Array.Empty<ProcInfo>());
        Assert.Equal(SingleKind.NoConflict, r.Kind);
    }

    [Fact]
    public void Taken_with_owner_in_candidates_returns_Found()
    {
        var pid = 2001;
        var key = (MOD, 0x78u);
        var closed = new HashSet<int>();
        var owner = new Dictionary<(uint, uint), int> { [key] = pid };

        var probe = new FakeProbe(owner, closed);
        var store = new DisabledListStore(Path.Combine(Path.GetTempPath(), "hk_s_" + Guid.NewGuid().ToString("N")[..8]));
        var lookup = new SingleLookup(probe, new FakeController(closed), store);

        var r = lookup.Lookup(key.Item1, key.Item2, new[] { Proc(pid, "x.exe") });
        Assert.Equal(SingleKind.Found, r.Kind);
        Assert.Equal(pid, r.OwnerPid);
    }

    [Fact]
    public void Taken_without_owner_returns_NotFound()
    {
        var key = (MOD, 0x79u);
        var closed = new HashSet<int>();
        var owner = new Dictionary<(uint, uint), int> { [key] = 9999 }; // 不在候选

        var probe = new FakeProbe(owner, closed);
        var store = new DisabledListStore(Path.Combine(Path.GetTempPath(), "hk_s_" + Guid.NewGuid().ToString("N")[..8]));
        var lookup = new SingleLookup(probe, new FakeController(closed), store);

        var r = lookup.Lookup(key.Item1, key.Item2, new[] { Proc(100, "y.exe") });
        Assert.Equal(SingleKind.NotFound, r.Kind);
    }

    // 复用 DeepScannerTests 的 Fake
    private sealed class FakeProbe : IHotkeyProbe
    {
        private readonly Dictionary<(uint, uint), int> _owner;
        private readonly HashSet<int> _closed;
        public FakeProbe(Dictionary<(uint, uint), int> o, HashSet<int> c) { _owner = o; _closed = c; }
        public ProbeResult Probe(uint m, uint v) =>
            !_owner.TryGetValue((m, v), out var pid) ? ProbeResult.Free
            : _closed.Contains(pid) ? ProbeResult.Free : ProbeResult.Taken;
    }

    private sealed class FakeController : IProcessController
    {
        private readonly HashSet<int> _closed;
        public FakeController(HashSet<int> c) { _closed = c; }
        public bool GracefulClose(int pid, TimeSpan t) { _closed.Add(pid); return true; }
        public bool ForceKill(int pid) { _closed.Add(pid); return true; }
        public Process? Restore(string p, string? a = null, string? d = null) => null;
    }
}
