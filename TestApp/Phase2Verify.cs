using System.Text;
using LiteAPI.Cache;

public static class Phase2Verify
{
    public static void Run()
    {
        Console.WriteLine("Phase2 verify: start");
        JustCache.Initialize();
        JustCache.ClearAll();

        // Keep a stable default capacity for tests unless explicitly testing eviction.
        JustCache.SetMaxItems(1000);

        VerifyLruEviction();
        VerifyTtlAndActiveExpiry();
        VerifyAofReplay();
        VerifyBinaryKeys();

        Console.WriteLine("Phase2 verify: OK");
    }

    private static void VerifyLruEviction()
    {
        JustCache.ClearAll();
        JustCache.SetMaxItems(2);

        JustCache.SetString("lru:k1", "v1");
        JustCache.SetString("lru:k2", "v2");

        // Touch k1 so k2 becomes LRU
        _ = JustCache.GetString("lru:k1");

        JustCache.SetString("lru:k3", "v3");

        var k2 = JustCache.GetString("lru:k2");
        if (k2 != null)
            throw new Exception("LRU eviction failed: expected k2 evicted");

        var k1 = JustCache.GetString("lru:k1");
        var k3 = JustCache.GetString("lru:k3");
        if (k1 != "v1" || k3 != "v3")
            throw new Exception("LRU eviction failed: expected k1/k3 present");

        // Reset for subsequent Phase2 verifications.
        JustCache.SetMaxItems(1000);
    }

    private static void VerifyTtlAndActiveExpiry()
    {
        JustCache.ClearAll();

        JustCache.SetStringWithTtl("ttl:k", "x", TimeSpan.FromMilliseconds(200));
        var v1 = JustCache.GetString("ttl:k");
        if (v1 != "x")
            throw new Exception("TTL failed: expected immediate hit");

        var ttl1 = JustCache.TtlMs("ttl:k");
        if (ttl1 is < 0 or > 2000)
            throw new Exception($"TTL failed: unexpected ttl value {ttl1}");

        Thread.Sleep(350);

        var v2 = JustCache.GetString("ttl:k");
        if (v2 != null)
            throw new Exception("TTL failed: expected expired miss");

        var ttl2 = JustCache.TtlMs("ttl:k");
        if (ttl2 != -2)
            throw new Exception($"TTL failed: expected -2 after expiry, got {ttl2}");
    }

    private static void VerifyAofReplay()
    {
        JustCache.ClearAll();

        var path = Path.Combine(Path.GetTempPath(), "justcache_phase2.aof");
        if (File.Exists(path))
            File.Delete(path);

        if (!JustCache.EnableAof(path))
            throw new Exception("AOF enable failed");

        JustCache.SetString("aof:k1", "1");
        JustCache.HSetString("aof:h", "name", "alice");
        JustCache.LPushString("aof:l", "x");
        _ = JustCache.SAddString("aof:s", "tag");
        JustCache.ZAdd("aof:z", 5, "m1");

        JustCache.DisableAof();

        // simulate restart (clear memory without logging)
        JustCache.ClearAll();

        if (!JustCache.LoadAof(path))
            throw new Exception("AOF load failed");

        if (JustCache.GetString("aof:k1") != "1")
            throw new Exception("AOF replay failed: string missing");

        if (JustCache.HGetString("aof:h", "name") != "alice")
            throw new Exception("AOF replay failed: hash missing");

        var list = JustCache.LRangeStrings("aof:l", 0, -1);
        if (list.Count != 1 || list[0] != "x")
            throw new Exception("AOF replay failed: list missing");

        if (!JustCache.SIsMemberString("aof:s", "tag"))
            throw new Exception("AOF replay failed: set missing");

        var zr = JustCache.ZRange("aof:z", 0, -1);
        if (zr.Count != 1 || zr[0] != "m1")
            throw new Exception("AOF replay failed: zset missing");
    }

    private static void VerifyBinaryKeys()
    {
        JustCache.ClearAll();

        byte[] key = [0, 1, 2, 255];
        byte[] val = Encoding.UTF8.GetBytes("bin");

        JustCache.Set(key, val);
        var got = JustCache.Get(key);
        if (got == null || Encoding.UTF8.GetString(got) != "bin")
            throw new Exception("Binary key set/get failed");

        JustCache.Remove(key);
        if (JustCache.Get(key) != null)
            throw new Exception("Binary key remove failed");
    }
}
