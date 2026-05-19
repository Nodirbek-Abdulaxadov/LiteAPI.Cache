using System.Diagnostics;
using LiteAPI.Cache;

var arg = args.FirstOrDefault() ?? "smoke";

JustCache.Initialize();

if (arg == "concurrent")
{
    RunConcurrent();
    return;
}

// Default smoke test (preserved for the existing CI matrix that invokes
// `phase1`/`phase2`/`phase3`/`phase4`).
JustCache.SetString("hello", "world");
Console.WriteLine($"hello -> {JustCache.GetString("hello")}");

JustCache.SetStringWithTtl("temp", "value", TimeSpan.FromMilliseconds(300));
Console.WriteLine($"temp ttl (ms) -> {JustCache.TtlMs("temp")}");

JustCache.Remove("hello");
Console.WriteLine($"hello removed -> {JustCache.GetString("hello") ?? "<null>"}");

JustCache.ClearAll();
Console.WriteLine("done");

return;

static void RunConcurrent()
{
    Console.WriteLine("# Concurrent throughput — validates the cache shards under contention.");
    Console.WriteLine("# Each row: total ops / wall-clock seconds = ops/sec.");
    Console.WriteLine();

    const int keyspace = 10_000;
    const int opsPerThread = 500_000;
    var value = new byte[64];
    Random.Shared.NextBytes(value);

    // Pre-encode all keys to byte[] so we measure cache work, not UTF-8.
    var keys = new byte[keyspace][];
    for (var i = 0; i < keyspace; i++)
    {
        keys[i] = System.Text.Encoding.UTF8.GetBytes($"k:{i:D6}");
    }

    // Warm the cache.
    JustCache.SetMaxItems(keyspace * 2);
    JustCache.ClearAll();
    for (var i = 0; i < keyspace; i++)
    {
        JustCache.Set(keys[i], value);
    }

    Console.WriteLine($"{"threads",-8} {"GET ops/s",-14} {"SET ops/s",-14} {"GET ns/op",-12} {"SET ns/op",-12}");

    foreach (var threads in new[] { 1, 2, 4, 8, 16 })
    {
        var totalGets = (long)threads * opsPerThread;
        var totalSets = (long)threads * opsPerThread;

        // GET (read-heavy: read locks on shards).
        var getTime = MeasureParallel(threads, opsPerThread, tid =>
        {
            var rng = new Random(unchecked(0x517cc1b7 ^ tid));
            var buf = new byte[64];
            for (var i = 0; i < opsPerThread; i++)
            {
                var k = keys[rng.Next(keyspace)];
                JustCache.TryGet(k, buf, out _);
            }
        });

        // SET (write-heavy: write locks on shards).
        var setTime = MeasureParallel(threads, opsPerThread, tid =>
        {
            var rng = new Random(unchecked((int)(0x9e3779b1u ^ (uint)tid)));
            for (var i = 0; i < opsPerThread; i++)
            {
                var k = keys[rng.Next(keyspace)];
                JustCache.Set(k, value);
            }
        });

        var getOps = totalGets / getTime.TotalSeconds;
        var setOps = totalSets / setTime.TotalSeconds;
        var getNs = (getTime.TotalNanoseconds) / totalGets;
        var setNs = (setTime.TotalNanoseconds) / totalSets;

        Console.WriteLine(
            $"{threads,-8} " +
            $"{getOps,14:N0} " +
            $"{setOps,14:N0} " +
            $"{getNs,12:N1} " +
            $"{setNs,12:N1}");
    }

    Console.WriteLine();
    Console.WriteLine("# Read aside: a perfectly sharded cache with no contention should");
    Console.WriteLine("# scale GET ops/s roughly linearly with threads up to NUM_SHARDS=16.");
    Console.WriteLine("# Any departure from linear is your sharding overhead + hash skew.");
}

static TimeSpan MeasureParallel(int threads, int opsPerThread, Action<int> body)
{
    var ready = new CountdownEvent(threads);
    var start = new ManualResetEventSlim(false);
    var done = new CountdownEvent(threads);
    var workers = new Thread[threads];

    for (var t = 0; t < threads; t++)
    {
        var tid = t;
        workers[t] = new Thread(() =>
        {
            ready.Signal();
            start.Wait();
            body(tid);
            done.Signal();
        }) { IsBackground = true };
        workers[t].Start();
    }

    ready.Wait();
    var sw = Stopwatch.StartNew();
    start.Set();
    done.Wait();
    sw.Stop();
    return sw.Elapsed;
}
