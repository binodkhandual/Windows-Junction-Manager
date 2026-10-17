using System.IO;

namespace JunctionManager;

/// Watches the parent folder of each watched junction (updaters delete/recreate the folder itself),
/// waits for the change to settle, then raises RepairRequested.
public sealed class WatcherService : IDisposable
{
    readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Timer> timers = new(StringComparer.OrdinalIgnoreCase);
    readonly Timer poll;
    List<JunctionEntry> entries = new();

    public event Action<JunctionEntry>? RepairRequested;
    public TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(20);

    public WatcherService()
    {
        // safety net: FileSystemWatcher can miss events
        poll = new Timer(_ => { foreach (var e in Snapshot()) Schedule(e); },
            null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
    }

    JunctionEntry[] Snapshot() { lock (this) return entries.ToArray(); }

    public void Sync(IEnumerable<JunctionEntry> all)
    {
        lock (this)
        {
            entries = all.Where(e => e.Watch).ToList();
            foreach (var w in watchers.Values) w.Dispose();
            watchers.Clear();

            foreach (var parent in entries.Select(e => Path.GetDirectoryName(e.Source))
                         .Where(p => !string.IsNullOrEmpty(p))
                         .Select(p => p!)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(parent)) continue;
                var w = new FileSystemWatcher(parent)
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                void Hit(string path)
                {
                    var leaf = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(leaf)) return;
                    foreach (var e in Snapshot())
                        if (Path.GetFileName(e.Source).Equals(leaf, StringComparison.OrdinalIgnoreCase)) Schedule(e);
                }
                w.Created += (_, a) => Hit(a.FullPath);
                w.Deleted += (_, a) => Hit(a.FullPath);
                w.Changed += (_, a) => Hit(a.FullPath);
                w.Renamed += (_, a) => { Hit(a.FullPath); Hit(a.OldFullPath); };
                watchers[parent] = w;
            }
        }
    }

    void Schedule(JunctionEntry e)
    {
        lock (timers)
        {
            if (timers.TryGetValue(e.Source, out var t)) t.Dispose();
            timers[e.Source] = new Timer(_ => Check(e), null, Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    void Check(JunctionEntry e)
    {
        var st = JunctionService.GetStatus(e);
        if (st is "OK" or "Target missing") return;
        if (st == "Unlinked")
        {
            // installer may still be writing: only repair once the folder stops changing
            var a = Snap(e.Source);
            Thread.Sleep(10_000);
            if (a != Snap(e.Source)) { Schedule(e); return; }
        }
        RepairRequested?.Invoke(e);
    }

    static (int, long) Snap(string path)
    {
        int n = 0; long b = 0;
        try { foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)) { n++; b += f.Length; } }
        catch { }
        return (n, b);
    }

    public void Dispose()
    {
        poll.Dispose();
        lock (this) foreach (var w in watchers.Values) w.Dispose();
        lock (timers) foreach (var t in timers.Values) t.Dispose();
    }
}
