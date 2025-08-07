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
}