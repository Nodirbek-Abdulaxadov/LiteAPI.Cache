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

    Console.WriteLine(
        $"{"threads",-8} {"TryGet ops/s",-14} {"TryPeek ops/s",-15} {"Set ops/s",-14} " +
        $"{"Get ns",-9} {"Peek ns",-9} {"Set ns",-9}");

    foreach (var threads in new[] { 1, 2, 4, 8, 16 })
    {
        var totalOps = (long)threads * opsPerThread;

        // TryGet: takes shard write lock (because LruCache::get mutates
        // recency). Serializes per shard even for reads.
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

        // TryPeek: takes shard *read* lock, no LRU update. Many threads
        // on the same shard can read in parallel.
        var peekTime = MeasureParallel(threads, opsPerThread, tid =>
        {
            var rng = new Random(unchecked((int)(0xa5a5a5a5u ^ (uint)tid)));
            var buf = new byte[64];
            for (var i = 0; i < opsPerThread; i++)
            {
                var k = keys[rng.Next(keyspace)];
                JustCache.TryPeek(k, buf, out _);
            }
        });

        // Set: write lock per shard.
        var setTime = MeasureParallel(threads, opsPerThread, tid =>
        {
            var rng = new Random(unchecked((int)(0x9e3779b1u ^ (uint)tid)));
            for (var i = 0; i < opsPerThread; i++)
            {
                var k = keys[rng.Next(keyspace)];
                JustCache.Set(k, value);
            }
        });

        Console.WriteLine(
            $"{threads,-8} " +
            $"{totalOps / getTime.TotalSeconds,14:N0} " +
            $"{totalOps / peekTime.TotalSeconds,15:N0} " +
            $"{totalOps / setTime.TotalSeconds,14:N0} " +
            $"{getTime.TotalNanoseconds / totalOps,9:N1} " +
            $"{peekTime.TotalNanoseconds / totalOps,9:N1} " +
            $"{setTime.TotalNanoseconds / totalOps,9:N1}");
    }

    Console.WriteLine();
    Console.WriteLine("# Reading:");
    Console.WriteLine("# - TryGet promotes LRU on each call → shard write lock → reads serialize");
    Console.WriteLine("#   per shard. Throughput plateau around the write-lock contention floor.");
    Console.WriteLine("# - TryPeek is the matching read-only path → shard read lock → many readers");
    Console.WriteLine("#   per shard run in parallel. This is where sharding actually pays off");
    Console.WriteLine("#   for read traffic. Should scale ~linearly to NUM_SHARDS=16.");
    Console.WriteLine("# - Set takes the write lock by construction; expect ~linear scaling up to");
    Console.WriteLine("#   NUM_SHARDS=16 modulo FFI and Random.Next overhead.");
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
