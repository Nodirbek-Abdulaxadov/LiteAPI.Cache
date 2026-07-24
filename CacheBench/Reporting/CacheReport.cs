using CacheBench.Caches;
using CacheBench.Harness;

namespace CacheBench.Reporting;

public static class Scenarios
{
    public const string Hit = "Hit (warm get)";
    public const string Miss = "Miss (get-or-compute)";
    public const string Set = "Set";
    public const string ConcurrentHit = "Concurrent hit";
    public const string Mixed = "Mixed 90/10";

    // Per-op scenarios shown in the latency/throughput tables.
    public static readonly string[] PerOp = { Hit, Miss, Set, ConcurrentHit, Mixed };
}

public sealed class CacheReport
{
    public string Timestamp { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Os { get; set; } = "";
    public string Cpu { get; set; } = "";
    public int LogicalCores { get; set; }
    public string Profile { get; set; } = "";
    public int Concurrency { get; set; }
    public int StampedeConcurrency { get; set; }
    public int MemoryEntries { get; set; }
    public int GcEntries { get; set; }

    public List<CacheResult> Results { get; } = new();     // per-op scenarios
    public List<CacheResult> Stampede { get; } = new();    // one row per cache
    public List<MemoryResult> Memory { get; } = new();
    public List<GcResult> Gc { get; } = new();             // GC pause under a large set

    public CacheResult? Get(string cache, string scenario) =>
        Results.FirstOrDefault(r => r.Cache == cache && r.Scenario == scenario);
    public CacheResult? Stamp(string cache) => Stampede.FirstOrDefault(r => r.Cache == cache);
    public MemoryResult? Mem(string cache) => Memory.FirstOrDefault(r => r.Cache == cache);
    public GcResult? GcOf(string cache) => Gc.FirstOrDefault(g => g.Cache == cache);
    public CacheFeatures Features(string cache) => CacheRegistry.FeaturesOf(cache);
}
