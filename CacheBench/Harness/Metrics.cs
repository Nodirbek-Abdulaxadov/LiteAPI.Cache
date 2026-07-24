namespace CacheBench.Harness;

// One timed result for a (cache, scenario) pair.
public sealed record CacheResult
{
    public required string Cache { get; init; }
    public required string Scenario { get; init; }
    public int Iterations { get; init; }
    public int Concurrency { get; init; } = 1;

    public double MeanNs { get; init; }         // per-operation latency
    public double OpsPerSec { get; init; }       // throughput
    public long AllocBytesPerOp { get; init; }
    public int Gen0 { get; init; }
    public int Gen1 { get; init; }
    public int Gen2 { get; init; }
    public double WallMs { get; init; }

    // Stampede-only: how many times the factory actually ran for one cold key.
    public int FactoryCalls { get; init; }
}

// GC pause cost while the cache holds a large, long-lived working set. Measured
// as a DELTA — forced-full-GC pause with the cache empty vs. holding N entries —
// so the shared key array and any unrelated process state cancel out and only
// the cache's own marginal contribution is reported. Off-heap caches add almost
// nothing to the managed graph, so their marginal pause is near zero.
public sealed record GcResult
{
    public required string Cache { get; init; }
    public int Entries { get; init; }
    public double EmptyPauseMs { get; init; }   // forced full-GC pause, cache empty (local reference)
    public double FullPauseMs { get; init; }    // forced full-GC pause, N entries held
    public long ManagedAddedBytes { get; init; } // managed-heap delta caused by the N entries
    public double RetainedFraction { get; init; } // 0..1 of entries still cached after the GCs

    public double MarginalPauseMs => Math.Max(0, FullPauseMs - EmptyPauseMs);
}

// Memory footprint after inserting N entries.
public sealed record MemoryResult
{
    public required string Cache { get; init; }
    public int Entries { get; init; }
    public long ManagedBytesTotal { get; init; }
    public long WorkingSetDelta { get; init; }
    public double ManagedBytesPerEntry => Entries == 0 ? 0 : (double)ManagedBytesTotal / Entries;
}
