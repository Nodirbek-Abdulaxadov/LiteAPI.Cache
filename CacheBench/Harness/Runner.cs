using System.Diagnostics;
using System.Runtime.CompilerServices;
using CacheBench.Caches;
using CacheBench.Workload;

namespace CacheBench.Harness;

// Async measurement harness: fixed warmup (100) then a measured iteration count,
// plus concurrent-throughput, stampede and memory measurements.
public static class Runner
{
    private static volatile int _sink;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume(string? s)
    {
        if (s is not null) _sink ^= s.Length;
    }

    // Single-threaded latency / throughput of one async op.
    public static async Task<CacheResult> MeasureAsync(
        string cache, string scenario, Func<int, ValueTask<string>> op, int warmup, int iterations)
    {
        for (int i = 0; i < warmup; i++) Consume(await op(i));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) Consume(await op(i));
        sw.Stop();

        long alloc1 = GC.GetAllocatedBytesForCurrentThread();
        double seconds = sw.Elapsed.TotalSeconds;

        return new CacheResult
        {
            Cache = cache,
            Scenario = scenario,
            Iterations = iterations,
            MeanNs = iterations == 0 ? 0 : sw.Elapsed.TotalNanoseconds / iterations,
            OpsPerSec = seconds <= 0 ? 0 : iterations / seconds,
            AllocBytesPerOp = iterations == 0 ? 0 : (alloc1 - alloc0) / iterations,
            Gen0 = GC.CollectionCount(0) - g0,
            Gen1 = GC.CollectionCount(1) - g1,
            Gen2 = GC.CollectionCount(2) - g2,
            WallMs = sw.Elapsed.TotalMilliseconds,
        };
    }

    // Aggregate throughput under P concurrent workers doing totalOps operations.
    public static async Task<CacheResult> MeasureConcurrentAsync(
        string cache, string scenario, Func<int, ValueTask<string>> op, int concurrency, int totalOps)
    {
        // warmup
        for (int i = 0; i < 200; i++) Consume(await op(i));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int perWorker = Math.Max(1, totalOps / concurrency);
        int actualTotal = perWorker * concurrency;

        var sw = Stopwatch.StartNew();
        var workers = new Task[concurrency];
        for (int w = 0; w < concurrency; w++)
        {
            int seed = w * perWorker;
            workers[w] = Task.Run(async () =>
            {
                for (int i = 0; i < perWorker; i++)
                    Consume(await op(seed + i));
            });
        }
        await Task.WhenAll(workers);
        sw.Stop();

        double seconds = sw.Elapsed.TotalSeconds;
        return new CacheResult
        {
            Cache = cache,
            Scenario = scenario,
            Iterations = actualTotal,
            Concurrency = concurrency,
            MeanNs = actualTotal == 0 ? 0 : sw.Elapsed.TotalNanoseconds / actualTotal,
            OpsPerSec = seconds <= 0 ? 0 : actualTotal / seconds,
            WallMs = sw.Elapsed.TotalMilliseconds,
        };
    }

    // Cache stampede: many concurrent GETs for one cold key. Reports factory-call
    // count (quality of protection) and total latency.
    public static async Task<CacheResult> MeasureStampedeAsync(
        string cache, ICacheAdapter adapter, Factory factory, int concurrency)
    {
        adapter.Reset();
        factory.ResetCalls();

        var sw = Stopwatch.StartNew();
        var tasks = new Task<string>[concurrency];
        for (int i = 0; i < concurrency; i++)
            tasks[i] = adapter.GetOrComputeAsync("hot:key").AsTask();
        await Task.WhenAll(tasks);
        sw.Stop();

        return new CacheResult
        {
            Cache = cache,
            Scenario = "Stampede",
            Iterations = concurrency,
            Concurrency = concurrency,
            MeanNs = sw.Elapsed.TotalNanoseconds / concurrency,
            WallMs = sw.Elapsed.TotalMilliseconds,
            FactoryCalls = factory.Calls,
        };
    }

    private static void CleanGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static double TimeForcedGcs(int rounds)
    {
        double total = 0;
        for (int r = 0; r < rounds; r++)
        {
            var sw = Stopwatch.StartNew();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            sw.Stop();
            total += sw.Elapsed.TotalMilliseconds;
        }
        return total / rounds;
    }

    // Marginal GC pause added by holding `entries` long-lived items. We measure a
    // forced full blocking GC with the cache EMPTY, then again after populating N
    // entries, in the same local process state — the difference isolates the
    // cache's own object graph (the shared key array and any prior state cancel).
    public static async Task<GcResult> MeasureGcPauseAsync(
        string cache, ICacheAdapter adapter, Factory factory, string[] keys, int entries, int rounds = 25)
    {
        adapter.Reset();
        CleanGc();
        long managedEmpty = GC.GetTotalMemory(true);
        double emptyPause = TimeForcedGcs(rounds);

        for (int i = 0; i < entries; i++)
            await adapter.GetOrComputeAsync(keys[i]);

        CleanGc();
        long managedFull = GC.GetTotalMemory(true);
        double fullPause = TimeForcedGcs(rounds);

        // Keep the working set rooted through every collection above — otherwise
        // the JIT could treat the cache as dead and the GC would have nothing to mark.
        GC.KeepAlive(adapter);
        GC.KeepAlive(keys);

        // Retention: after those collections, re-read every key and count how many
        // trigger the factory again. A cache that dodged GC pause by evicting its
        // entries (e.g. weak references) reveals itself here with a low fraction.
        factory.ResetCalls();
        for (int i = 0; i < entries; i++)
            await adapter.GetOrComputeAsync(keys[i]);
        double retained = entries == 0 ? 1.0 : 1.0 - (double)factory.Calls / entries;

        return new GcResult
        {
            Cache = cache,
            Entries = entries,
            EmptyPauseMs = emptyPause,
            FullPauseMs = fullPause,
            ManagedAddedBytes = Math.Max(0, managedFull - managedEmpty),
            RetainedFraction = Math.Clamp(retained, 0, 1),
        };
    }

    // Managed-heap + working-set cost of holding `entries` items.
    public static async Task<MemoryResult> MeasureMemoryAsync(
        string cache, ICacheAdapter adapter, string[] keys, int entries)
    {
        adapter.Reset();
        var proc = Process.GetCurrentProcess();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long managed0 = GC.GetTotalMemory(true);
        proc.Refresh();
        long ws0 = proc.WorkingSet64;

        for (int i = 0; i < entries; i++)
            await adapter.GetOrComputeAsync(keys[i]);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long managed1 = GC.GetTotalMemory(true);
        proc.Refresh();
        long ws1 = proc.WorkingSet64;

        return new MemoryResult
        {
            Cache = cache,
            Entries = entries,
            ManagedBytesTotal = Math.Max(0, managed1 - managed0),
            WorkingSetDelta = Math.Max(0, ws1 - ws0),
        };
    }
}
