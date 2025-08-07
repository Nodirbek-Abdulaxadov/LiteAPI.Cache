using System.Diagnostics;
using System.Text.Json;
using StackExchange.Redis;

public class RedisTest
{
    private static IDatabase redisDb = null!;

    public static List<(string title, string setElapsed, string getElapsed)> Start()
    {
        // Connect to Redis server
        ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost:6379");
        redisDb = redis.GetDatabase();

        List<(string title, string setElapsed, string getElapsed)> results = [];

        // Run tests with different cache sizes
        var test1 = TestCachePerformance(10);
        results.Add(("Redis with 10 items", test1.set, test1.get));

        var test2 = TestCachePerformance(100);
        results.Add(("Redis with 100 items", test2.set, test2.get));

        var test3 = TestCachePerformance(1000);
        results.Add(("Redis with 1000 items", test3.set, test3.get));

        var test4 = TestCachePerformance(100000);
        results.Add(("Redis with 100000 items", test4.set, test4.get));

        var test5 = TestCachePerformance(1000000);
        results.Add(("Redis with 1000000 items", test5.set, test5.get));

        // Cleanup
        Stopwatch stopwatch = Stopwatch.StartNew();
        redisDb.Execute("FLUSHDB");
        stopwatch.Stop();
        var cleanupElapsed = stopwatch.ElapsedMicroseconds().ToString();
        results.Add(("Redis Cleanup", cleanupElapsed, cleanupElapsed));

        return results;
    }

    // Helper method to perform set and get tests for Redis
    static (string set, string get) TestCachePerformance(int itemCount)
    {
        string key = $"students:{itemCount}";
        var students = Enumerable.Range(1, itemCount).Select(i => new Student { Id = i, Name = $"Student {i}" }).ToList();

        // Serialize students to JSON string
        var json = JsonSerializer.Serialize(students);

        // Set students to Redis cache
        Stopwatch stopwatch = Stopwatch.StartNew();
        redisDb.StringSet(key, json);
        stopwatch.Stop();
        var setElapsed = stopwatch.ElapsedMicroseconds().ToString();

        // Get students from Redis cache
        stopwatch.Restart();
        var cachedJson = redisDb.StringGet(key);
        stopwatch.Stop();
        var getElapsed = stopwatch.ElapsedMicroseconds().ToString();

        return (setElapsed, getElapsed);
    }
}