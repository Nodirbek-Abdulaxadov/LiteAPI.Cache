using System.Text;
using LiteAPI.Cache;

public static class Phase1Verify
{
    public static void Run()
    {
        Console.WriteLine("Phase1 verify: start");
        JustCache.Initialize();
        JustCache.ClearAll();

        VerifyHashes();
        VerifyLists();
        VerifySets();
        VerifySortedSets();

        Console.WriteLine("Phase1 verify: OK");
    }

    private static void VerifyHashes()
    {
        const string key = "h:student:1";
        JustCache.HSetString(key, "name", "Alice");
        JustCache.HSetString(key, "city", "Tashkent");

        var name = JustCache.HGetString(key, "name");
        if (name != "Alice")
            throw new Exception($"HGET failed: {name}");

        var all = JustCache.HGetAll(key);
        if (!all.TryGetValue("city", out var cityBytes))
            throw new Exception("HGETALL missing city");

        var city = Encoding.UTF8.GetString(cityBytes);
        if (city != "Tashkent")
            throw new Exception($"HGETALL failed: {city}");
    }

    private static void VerifyLists()
    {
        const string key = "l:recent";
        JustCache.LPushString(key, "a");
        JustCache.LPushString(key, "b");
        JustCache.LPushString(key, "c");

        var range = JustCache.LRangeStrings(key, 0, -1);
        if (range.Count != 3 || range[0] != "c" || range[2] != "a")
            throw new Exception($"LRANGE failed: [{string.Join(",", range)}]");

        var popped = JustCache.RPopString(key);
        if (popped != "a")
            throw new Exception($"RPOP failed: {popped}");
    }

    private static void VerifySets()
    {
        const string key = "s:tags";
        var added1 = JustCache.SAddString(key, "x");
        var added2 = JustCache.SAddString(key, "x");

        if (!added1)
            throw new Exception("SADD should add first time");
        if (added2)
            throw new Exception("SADD should not add duplicate");

        if (!JustCache.SIsMemberString(key, "x"))
            throw new Exception("SISMEMBER should be true");
        if (JustCache.SIsMemberString(key, "y"))
            throw new Exception("SISMEMBER should be false");
    }

    private static void VerifySortedSets()
    {
        const string key = "z:lb";
        JustCache.ZAdd(key, 10, "bob");
        JustCache.ZAdd(key, 5, "alice");
        JustCache.ZAdd(key, 7, "carl");

        var members = JustCache.ZRange(key, 0, -1);
        if (members.Count != 3 || members[0] != "alice" || members[2] != "bob")
            throw new Exception($"ZRANGE failed: [{string.Join(",", members)}]");
    }
}
