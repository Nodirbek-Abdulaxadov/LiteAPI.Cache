using System.Collections.Concurrent;
using System.Diagnostics;
using LiteAPI.Cache;

public class ConcurrencyTest
{
	private static ConcurrentBag<(string title, string setElapsed, string getElapsed)> justCacheResults = [];
    
    public static List<(string title, string setElapsed, string getElapsed)> Start()
    {
        JustCache.Initialize();
        justCacheResults.Clear();

        var task1 = TestCachePerformance(10);
        justCacheResults.Add(task1);
        var task2 = TestCachePerformance(100);
        justCacheResults.Add(task2);
        var task3 = TestCachePerformance(1000);
        justCacheResults.Add(task3);
        var task4 = TestCachePerformance(100000);
        justCacheResults.Add(task4);
        var task5 = TestCachePerformance(1000000);
        justCacheResults.Add(task5);

        // Mixed stress (read/write/remove concurrently)
        var stress = MixedStress(durationSeconds: 5, keyCount: 10_000);
        justCacheResults.Add((
            "JustCache MixedStress 5s",
            stress.setOps.ToString(),
            stress.getOps.ToString()
        ));

        // Cleanup
        Stopwatch stopwatch = Stopwatch.StartNew();
        JustCache.ClearAll();
        stopwatch.Stop();
        var cleanupElapsed = stopwatch.ElapsedMicroseconds();
        justCacheResults.Add(("JustCachex Cleanup", cleanupElapsed.ToString(), cleanupElapsed.ToString()));

        return [.. justCacheResults.OrderBy(x => x.title)];
    }

    public static (string title, string setElapsed, string getElapsed) TestCachePerformance(int iterations)
    {
        JustCache.Initialize();
        
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var title = $"JustCache Concurrent {iterations}";

        var tasks = Enumerable.Range(0, iterations).Select(i => Task.Run(() =>
        {
            string key = Guid.NewGuid().ToString();
            string value = Guid.NewGuid().ToString();
            JustCache.SetString(key, value);
            var retrievedValue = JustCache.GetString(key);
        })).ToArray();

        Task.WaitAll(tasks);
        stopwatch.Stop();
        return (title, stopwatch.ElapsedMicroseconds(), stopwatch.ElapsedMicroseconds());
    }

    private static (long setOps, long getOps, long removeOps, long misses, long invalid) MixedStress(int durationSeconds, int keyCount)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        var token = cts.Token;
        var rnd = new Random();

        // Prepare keys
        var keys = Enumerable.Range(0, keyCount).Select(_ => Guid.NewGuid().ToString()).ToArray();
        var versions = new ConcurrentDictionary<string, int>();

        // Preload
        foreach (var k in keys)
        {
            JustCache.SetString(k, $"{k}:0");
            versions[k] = 0;
        }

        long setOps = 0, getOps = 0, removeOps = 0, misses = 0, invalid = 0;

        int writerThreads = Math.Max(2, Environment.ProcessorCount / 4);
        int readerThreads = Math.Max(4, Environment.ProcessorCount / 2);
        int removerThreads = 1;

        var writers = Enumerable.Range(0, writerThreads).Select(_ => Task.Run(() =>
        {
            var localRnd = new Random();
            while (!token.IsCancellationRequested)
            {
                var k = keys[localRnd.Next(keys.Length)];
                var v = versions.AddOrUpdate(k, _ => 1, (_, old) => old + 1);
                JustCache.SetString(k, $"{k}:{v}");
                Interlocked.Increment(ref setOps);
            }
        }, token)).ToArray();

        var readers = Enumerable.Range(0, readerThreads).Select(_ => Task.Run(() =>
        {
            var localRnd = new Random();
            while (!token.IsCancellationRequested)
            {
                var k = keys[localRnd.Next(keys.Length)];
                var s = JustCache.GetString(k);
                if (s == null)
                {
                    Interlocked.Increment(ref misses);
                }
                else
                {
                    // Basic shape check: should start with key and contain ':'
                    if (!s.StartsWith(k) || !s.Contains(':'))
                        Interlocked.Increment(ref invalid);
                }
                Interlocked.Increment(ref getOps);
            }
        }, token)).ToArray();

        var removers = Enumerable.Range(0, removerThreads).Select(_ => Task.Run(() =>
        {
            var localRnd = new Random();
            while (!token.IsCancellationRequested)
            {
                var k = keys[localRnd.Next(keys.Length)];
                JustCache.Remove(k);
                Interlocked.Increment(ref removeOps);
            }
        }, token)).ToArray();

        Task.WaitAll(writers.Concat(readers).Concat(removers).ToArray());

        return (setOps, getOps, removeOps, misses, invalid);
    }
}