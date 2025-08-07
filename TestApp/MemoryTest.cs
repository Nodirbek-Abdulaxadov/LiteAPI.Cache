using System.Diagnostics;
using System.Text.Json;
using LiteAPI.Cache;

public class MemoryTest
{
    private static Dictionary<string, string> memoryCache = [];

    public static List<(string title, string setElapsed, string getElapsed)> Start()
    {
        List<(string title, string setElapsed, string getElapsed)> results = [];

        // Run tests with different cache sizes
        var test1 = TestCachePerformance(10);
        results.Add(("MemoryCache with 10 items", test1.set, test1.get));
        var test2 = TestCachePerformance(100);
        results.Add(("MemoryCache with 100 items", test2.set, test2.get));
        var test3 = TestCachePerformance(1000);
        results.Add(("MemoryCache with 1000 items", test3.set, test3.get));
        var test4 = TestCachePerformance(100000);
        results.Add(("MemoryCache with 100000 items", test4.set, test4.get));
        var test5 = TestCachePerformance(1000000);
        results.Add(("MemoryCache with 1000000 items", test5.set, test5.get));

        //cleanup
        Stopwatch stopwatch = Stopwatch.StartNew();
        memoryCache.Clear();
        stopwatch.Stop();
        var cleanupElapsed = stopwatch.ElapsedMicroseconds();
        results.Add(("MemoryCache Cleanup", cleanupElapsed.ToString(), cleanupElapsed.ToString()));

        return results;
    }

    // Helper method to perform set and get tests for cache
    static (string set, string get) TestCachePerformance(int itemCount)
    {
        string key = $"students{itemCount}";
        var students = Enumerable.Range(1, itemCount).Select(i => Student.Random(i)).ToList();
        
        // Serialize students to JSON string
        var json = JsonSerializer.Serialize(students);

        // Set students to cache
        Stopwatch stopwatch = Stopwatch.StartNew();
        memoryCache[key] = json;
        stopwatch.Stop();
        var setElapsed = stopwatch.ElapsedMicroseconds();

        // Get students from cache
        stopwatch.Restart();
        var cachedJson = memoryCache[key];
        stopwatch.Stop();
        var getElapsed = stopwatch.ElapsedMicroseconds();

        return (setElapsed, getElapsed);
    }
}