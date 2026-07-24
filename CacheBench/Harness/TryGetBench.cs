using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using LiteAPI.Cache;

namespace CacheBench.Harness;

// Focused validation of LiteAPI.Cache's GC-free read path
// TryGet(byte[] key, Span<byte> destination, out int written).
// 2.6.0 reads it under the shard READ lock with sampled LRU recency, so it
// should scale better across cores. Object-returning reads (Get/GetString) are
// unchanged and only serve as a regression guard here.
public static class TryGetBench
{
    private static volatile int _sink;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume(int v) => _sink ^= v;

    public static void Run()
    {
        int cores = Environment.ProcessorCount;
        const int workingSet = 100_000;
        const int valueBytes = 256;
        const int stIters = 5_000_000;              // single-thread iterations

        var ver = Assembly.Load("LiteAPI.Cache").GetName().Version;
        Console.WriteLine($"LiteAPI.Cache {ver} — TryGet read-path validation");
        PrintNativeModules();
        Console.WriteLine($"Working set {workingSet:N0} keys · value {valueBytes} B · cores {cores}");
        Console.WriteLine();

        JustCache.Initialize();
        JustCache.SetMaxItems(2_000_000);
        JustCache.ClearAll();

        // Pre-encoded byte[] keys + a value.
        var keys = new byte[workingSet][];
        for (int i = 0; i < workingSet; i++)
            keys[i] = Encoding.UTF8.GetBytes("k:" + i.ToString("D6"));
        var value = new byte[valueBytes];
        for (int i = 0; i < valueBytes; i++) value[i] = (byte)('a' + (i % 26));
        var stringKeys = new string[workingSet];
        for (int i = 0; i < workingSet; i++) stringKeys[i] = "k:" + i.ToString("D6");

        // Populate both key shapes so TryGet(byte[]) and GetString(string) both hit.
        for (int i = 0; i < workingSet; i++)
        {
            JustCache.Set(keys[i], value);
            JustCache.Set(stringKeys[i], value);
        }

        // ---- single-thread ----
        double stTryGet = SingleThreadTryGet(keys, workingSet, stIters, peek: false);
        double stTryPeek = SingleThreadTryGet(keys, workingSet, stIters, peek: true);
        var (gsNs, gsAlloc) = SingleThreadGetString(stringKeys, workingSet, 2_000_000);
        Console.WriteLine("[single-thread]");
        Console.WriteLine($"  TryGet    : {stTryGet,7:0.0} ns/op");
        Console.WriteLine($"  TryPeek   : {stTryPeek,7:0.0} ns/op   (ceiling)");
        Console.WriteLine($"  GetString : {gsNs,7:0.0} ns/op, {gsAlloc} B/op   (regression guard — unchanged in 2.6.0)");
        Console.WriteLine();

        // ---- concurrent scaling sweep ----
        int[] levels = BuildLevels(cores);
        const int perThreadSweep = 3_000_000;
        Console.WriteLine("[concurrent scaling — TryGet vs TryPeek ceiling, M ops/s]");
        Console.WriteLine("  threads |   TryGet | TryPeek | TryGet/single");
        double single = 0;
        var csvParts = new List<string>();
        foreach (int th in levels)
        {
            double tg = ConcurrentTryGet(keys, workingSet, perThreadSweep, th, peek: false) / 1e6;
            double tp = ConcurrentTryGet(keys, workingSet, perThreadSweep, th, peek: true) / 1e6;
            if (th == 1) single = tg;
            double scale = single > 0 ? tg / single : 1;
            Console.WriteLine($"  {th,7} | {tg,8:0.00} | {tp,7:0.00} | {scale,6:0.0}×");
            csvParts.Add($"t{th}={tg:0.00}");
        }
        Console.WriteLine();

        // ---- high-contention: all cores hammer a small hot key set (hot shards) ----
        int[] hotSets = { 8, 64, 512, workingSet };
        Console.WriteLine($"[high contention @ {cores} cores — TryGet vs TryPeek, M ops/s]");
        Console.WriteLine("  hot keys |   TryGet | TryPeek");
        var csvHot = new List<string>();
        foreach (int hot in hotSets)
        {
            double tg = ConcurrentTryGet(keys, hot, perThreadSweep, cores, peek: false) / 1e6;
            double tp = ConcurrentTryGet(keys, hot, perThreadSweep, cores, peek: true) / 1e6;
            string label = hot == workingSet ? $"{hot} (spread)" : hot.ToString();
            Console.WriteLine($"  {label,8} | {tg,8:0.00} | {tp,7:0.00}");
            csvHot.Add($"hot{hot}={tg:0.00}");
        }
        Console.WriteLine();
        Console.WriteLine($"CSV,{ver},st_tryget_ns={stTryGet:0.0},gs_ns={gsNs:0.0},gs_alloc={gsAlloc}," + string.Join(",", csvParts) + "," + string.Join(",", csvHot));
    }

    private static double SingleThreadTryGet(byte[][] keys, int n, int iters, bool peek)
    {
        Span<byte> buffer = stackalloc byte[1024];
        for (int i = 0; i < 100_000; i++) // warmup
        {
            if (peek) JustCache.TryPeek(keys[i % n], buffer, out int w0); else JustCache.TryGet(keys[i % n], buffer, out int w0);
        }
        var sw = Stopwatch.StartNew();
        int acc = 0;
        for (int i = 0; i < iters; i++)
        {
            int w;
            bool ok = peek ? JustCache.TryPeek(keys[i % n], buffer, out w) : JustCache.TryGet(keys[i % n], buffer, out w);
            acc += ok ? w : 0;
        }
        sw.Stop();
        Consume(acc);
        return sw.Elapsed.TotalNanoseconds / iters;
    }

    private static (double ns, long alloc) SingleThreadGetString(string[] keys, int n, int iters)
    {
        for (int i = 0; i < 50_000; i++) Consume(JustCache.GetString(keys[i % n])?.Length ?? 0);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        int acc = 0;
        for (int i = 0; i < iters; i++) acc += JustCache.GetString(keys[i % n])?.Length ?? 0;
        sw.Stop();
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        Consume(acc);
        return (sw.Elapsed.TotalNanoseconds / iters, (a1 - a0) / iters);
    }

    // keyRange < n concentrates reads on a few hot shards (high lock contention);
    // keyRange == n spreads them out (low contention).
    private static double ConcurrentTryGet(byte[][] keys, int keyRange, int perThread, int threads, bool peek)
    {
        Span<byte> warm = stackalloc byte[1024];
        for (int i = 0; i < 100_000; i++) JustCache.TryGet(keys[i % keyRange], warm, out _);

        var tasks = new Task[threads];
        var sw = Stopwatch.StartNew();
        for (int t = 0; t < threads; t++)
        {
            int seed = t * 9973;
            tasks[t] = Task.Run(() =>
            {
                Span<byte> buffer = stackalloc byte[1024];
                int acc = 0;
                for (int i = 0; i < perThread; i++)
                {
                    int idx = (seed + i) % keyRange;
                    int w;
                    bool ok = peek ? JustCache.TryPeek(keys[idx], buffer, out w) : JustCache.TryGet(keys[idx], buffer, out w);
                    acc += ok ? w : 0;
                }
                Consume(acc);
            });
        }
        Task.WaitAll(tasks);
        sw.Stop();
        return (double)perThread * threads / sw.Elapsed.TotalSeconds;
    }

    private static int[] BuildLevels(int cores)
    {
        var set = new SortedSet<int>();
        foreach (int c in new[] { 1, 2, 4, 6, 8, cores })
            if (c >= 1 && c <= cores) set.Add(c);
        return set.ToArray();
    }

    private static void PrintNativeModules()
    {
        try
        {
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                string name = m.ModuleName?.ToLowerInvariant() ?? "";
                if (name.Contains("liteapi") || name.Contains("justcache") || name.Contains("lite_api") || name.Contains("litecache"))
                    Console.WriteLine($"  native module: {m.ModuleName}  ({m.FileName}) v{m.FileVersionInfo.FileVersion}");
            }
        }
        catch { /* best-effort */ }
    }
}
