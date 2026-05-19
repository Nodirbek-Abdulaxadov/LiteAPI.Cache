using System.Text;
using System.Threading;
using LiteAPI.Cache;
using Xunit;

namespace LiteAPI.Cache.IntegrationTests;

[Collection("JustCacheCollection")]
public sealed class JustCacheIntegrationTests
{
    private readonly JustCacheFixture _fixture;

    public JustCacheIntegrationTests(JustCacheFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        var start = DateTime.UtcNow;
        var delay = pollInterval ?? TimeSpan.FromMilliseconds(25);

        while (DateTime.UtcNow - start < timeout)
        {
            if (condition())
                return true;

            Thread.Sleep(delay);
        }

        return false;
    }

    [Fact]
    public void SetGetRemoveClear_Works()
    {
        JustCache.SetString("core:k1", "v1");
        Assert.Equal("v1", JustCache.GetString("core:k1"));

        Assert.True(JustCache.TryGetString("core:k1", out var value));
        Assert.Equal("v1", value);

        JustCache.Remove("core:k1");
        Assert.Null(JustCache.GetString("core:k1"));

        JustCache.SetString("core:k2", "v2");
        JustCache.ClearAll();
        Assert.Null(JustCache.GetString("core:k2"));
    }

    private sealed record Person(int Id, string Name);

    [Fact]
    public void ObjectSerialization_Works()
    {
        var person = new Person(7, "Ada");
        JustCache.SetObject("obj:one", person);
        var loaded = JustCache.GetObject<Person>("obj:one");

        Assert.Equal(person, loaded);

        var list = new[] { new Person(1, "A"), new Person(2, "B") };
        JustCache.SetObjects("obj:list", list);
        var loadedList = JustCache.GetObjects<Person>("obj:list")?.ToList();

        Assert.NotNull(loadedList);
        Assert.Equal(list, loadedList);
    }

    [Fact]
    public void Hashes_Work()
    {
        JustCache.HSetString("hash:user", "name", "Alice");
        JustCache.HSetString("hash:user", "city", "Tashkent");

        Assert.Equal("Alice", JustCache.HGetString("hash:user", "name"));

        var all = JustCache.HGetAll("hash:user");
        Assert.Equal(2, all.Count);
        Assert.Equal("Alice", Encoding.UTF8.GetString(all["name"]));
        Assert.Equal("Tashkent", Encoding.UTF8.GetString(all["city"]));
    }

    [Fact]
    public void Lists_Work()
    {
        JustCache.LPushString("list:recent", "a");
        JustCache.LPushString("list:recent", "b");

        var popped = JustCache.RPopString("list:recent");
        Assert.Equal("a", popped);

        var items = JustCache.LRangeStrings("list:recent", 0, -1);
        Assert.Single(items);
        Assert.Equal("b", items[0]);
    }

    [Fact]
    public void Sets_Work()
    {
        Assert.True(JustCache.SAddString("set:tags", "x"));
        Assert.True(JustCache.SIsMemberString("set:tags", "x"));
        Assert.False(JustCache.SIsMemberString("set:tags", "y"));
    }

    [Fact]
    public void SortedSets_Work()
    {
        JustCache.ZAdd("z:leader", 5, "alice");
        JustCache.ZAdd("z:leader", 10, "bob");
        JustCache.ZAdd("z:leader", 7, "carol");

        var members = JustCache.ZRange("z:leader", 0, -1);
        Assert.Equal(new[] { "alice", "carol", "bob" }, members);
    }

    [Fact]
    public void LruEviction_Works()
    {
        // The cache is internally sharded: SetMaxItems(N) gives each
        // shard ceil(N / shardCount) slots, so LRU semantics are per-
        // shard, not global. To exercise LRU we generate many keys
        // mapped (probabilistically) to the same shard and assert that
        // recently-touched ones survive while older untouched ones are
        // evicted as the shard fills.
        const int cap = 32;
        JustCache.SetMaxItems(cap);

        // Touch a few "hot" keys repeatedly while spamming cold ones.
        var hot = new[] { "lru:hot:a", "lru:hot:b", "lru:hot:c" };
        foreach (var k in hot) JustCache.SetString(k, "hot");

        for (var i = 0; i < cap * 8; i++)
        {
            JustCache.SetString($"lru:cold:{i}", i.ToString());
            // Refresh the hot keys so they stay near the front of the LRU.
            foreach (var k in hot) JustCache.GetString(k);
        }

        // Hot keys must still be in the cache. (Strong: they were touched
        // after every cold insert, so an LRU policy keeps them.)
        foreach (var k in hot)
        {
            Assert.NotNull(JustCache.GetString(k));
        }

        // At least some cold keys must have been evicted. (Weak: we
        // inserted 8× capacity of cold keys, so even with hash skew the
        // shards must spill.)
        var coldMisses = 0;
        for (var i = 0; i < cap * 8; i++)
        {
            if (JustCache.GetString($"lru:cold:{i}") is null) coldMisses++;
        }
        Assert.True(coldMisses > 0, "expected at least one cold eviction");
    }

    [Fact]
    public void TtlAndExpire_Work()
    {
        JustCache.SetStringWithTtl("ttl:k1", "v", TimeSpan.FromMilliseconds(200));

        var ttl = JustCache.TtlMs("ttl:k1");
        Assert.True(ttl >= 0);

        Assert.True(WaitUntil(() => JustCache.GetString("ttl:k1") is null, TimeSpan.FromSeconds(2)));
        Assert.Equal(-2, JustCache.TtlMs("ttl:k1"));

        JustCache.SetString("ttl:k2", "v2");
        Assert.Equal(-1, JustCache.TtlMs("ttl:k2"));

        Assert.True(JustCache.Expire("ttl:k2", TimeSpan.FromMilliseconds(150)));
        Assert.True(WaitUntil(() => JustCache.GetString("ttl:k2") is null, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void AofReplay_Works()
    {
        var path = Path.Combine(Path.GetTempPath(), $"justcache_{Guid.NewGuid():N}.aof");

        try
        {
            Assert.True(JustCache.EnableAof(path));
            JustCache.SetString("aof:k1", "1");
            JustCache.DisableAof();

            JustCache.ClearAll();
            Assert.True(JustCache.LoadAof(path));
            Assert.Equal("1", JustCache.GetString("aof:k1"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void BinaryKeys_Work()
    {
        byte[] key = [0, 1, 2, 255];
        byte[] val = [5, 6, 7];

        JustCache.Set(key, val);
        var got = JustCache.Get(key);

        Assert.NotNull(got);
        Assert.Equal(val, got);

        JustCache.Remove(key);
        Assert.Null(JustCache.Get(key));
    }

    [Fact]
    public void PubSub_Works()
    {
        var sub = JustCache.Subscribe("chan:orders");
        try
        {
            JustCache.PublishString("chan:orders", "created:1");

            JustCache.PubSubMessage msg = default;
            var ok = WaitUntil(() => JustCache.TryPoll(sub, out msg), TimeSpan.FromSeconds(2));

            Assert.True(ok);
            Assert.Equal("chan:orders", msg.Channel);
            Assert.Equal("created:1", msg.PayloadAsString());
        }
        finally
        {
            JustCache.Unsubscribe(sub);
        }
    }

    [Fact]
    public void Notifications_Evicted_And_Expired()
    {
        JustCache.ClearNotifications();

        // Cache is sharded internally; SetMaxItems is approximate per-shard
        // (each shard gets ceil(N / shardCount) slots). Push enough keys
        // through to be sure at least one shard overflows. 256 inserts vs
        // a max of 16 leaves plenty of overflow regardless of hash skew.
        JustCache.SetMaxItems(16);
        for (var i = 0; i < 256; i++)
        {
            JustCache.SetString($"notify:k{i}", i.ToString());
        }

        JustCache.KeyspaceNotification eviction = default;
        var gotEviction = WaitUntil(
            () => JustCache.TryPollNotification(out eviction) && eviction.Kind == JustCache.NotificationKind.Evicted,
            TimeSpan.FromSeconds(2));

        Assert.True(gotEviction);
        Assert.False(string.IsNullOrWhiteSpace(eviction.Key));

        JustCache.ClearNotifications();
        JustCache.SetStringWithTtl("notify:ttl", "v", TimeSpan.FromMilliseconds(120));

        JustCache.KeyspaceNotification expired = default;
        var gotExpired = WaitUntil(
            () => JustCache.TryPollNotification(out expired) && expired.Kind == JustCache.NotificationKind.Expired,
            TimeSpan.FromSeconds(3));

        Assert.True(gotExpired);
        Assert.Equal("notify:ttl", expired.Key);
    }

    [Fact]
    public void Streams_Work()
    {
        var id1 = JustCache.XAdd("stream:orders", Encoding.UTF8.GetBytes("a"));
        var id2 = JustCache.XAdd("stream:orders", Encoding.UTF8.GetBytes("b"));

        var items = JustCache.XRange("stream:orders", id1, id2);
        Assert.True(items.Count >= 2);

        Assert.Equal(id1, items[0].Id);
        Assert.Equal("a", Encoding.UTF8.GetString(items[0].Payload));
        Assert.Equal(id2, items[1].Id);
        Assert.Equal("b", Encoding.UTF8.GetString(items[1].Payload));
    }

    [Fact]
    public void JsonPath_And_Index_And_Eval_Work()
    {
        JustCache.SetString("json:1", "{\"name\":\"a\",\"age\":10,\"tags\":[\"x\"]}");

        Assert.Equal("10", JustCache.JsonGetString("json:1", "$.age"));
        Assert.True(JustCache.JsonSet("json:1", "$.age", "11"));
        Assert.Equal("11", JustCache.JsonGetString("json:1", "$.age"));

        Assert.True(JustCache.CreateNumericIndex("age"));

        JustCache.SetString("json:p1", "{\"age\":10}");
        JustCache.SetString("json:p2", "{\"age\":20}");

        var keys = JustCache.FindKeys("age >= 18");
        Assert.Contains("json:p2", keys);
        Assert.DoesNotContain("json:p1", keys);

        Assert.Equal("OK", JustCache.EvalString("SET eval:k1 hello"));
        Assert.Equal("hello", JustCache.EvalString("GET eval:k1"));
        Assert.Equal("1", JustCache.EvalString("DEL eval:k1"));
    }

    [Fact]
    public void TryPeek_Mirrors_TryGet_For_Hits_And_Misses()
    {
        var key = Encoding.UTF8.GetBytes("peek:single");
        var payload = Encoding.UTF8.GetBytes("hello-peek");
        var buffer = new byte[64];

        // Miss before write.
        Assert.False(JustCache.TryPeek(key, buffer, out var missWritten));
        Assert.Equal(0, missWritten);

        JustCache.Set(key, payload);

        // Hit after write.
        Assert.True(JustCache.TryPeek(key, buffer, out var hitWritten));
        Assert.Equal(payload.Length, hitWritten);
        Assert.Equal(payload, buffer.AsSpan(0, hitWritten).ToArray());

        // TryGet sees the same value.
        var getBuf = new byte[64];
        Assert.True(JustCache.TryGet(key, getBuf, out var getWritten));
        Assert.Equal(payload, getBuf.AsSpan(0, getWritten).ToArray());

        // Buffer-too-small surfaces the required size on both APIs.
        var tinyBuf = new byte[1];
        Assert.False(JustCache.TryPeek(key, tinyBuf, out var requiredPeek));
        Assert.Equal(payload.Length, requiredPeek);
        Assert.False(JustCache.TryGet(key, tinyBuf, out var requiredGet));
        Assert.Equal(payload.Length, requiredGet);
    }

    [Fact]
    public void Concurrent_SetGet_Survives_16_Threads()
    {
        // Validates that 16 parallel writers + readers across keys
        // mapped to all 16 shards don't corrupt each other. Each thread
        // owns a disjoint key range, so the final state must contain
        // exactly the value it last wrote.
        const int threads = 16;
        const int opsPerThread = 5_000;

        JustCache.SetMaxItems(threads * opsPerThread * 2);
        JustCache.ClearAll();

        var workers = new Thread[threads];
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        for (var t = 0; t < threads; t++)
        {
            var tid = t;
            workers[t] = new Thread(() =>
            {
                var buf = new byte[32];
                for (var i = 0; i < opsPerThread; i++)
                {
                    var key = Encoding.UTF8.GetBytes($"conc:t{tid}:k{i}");
                    var val = Encoding.UTF8.GetBytes($"v-{tid}-{i}");

                    JustCache.Set(key, val);

                    // Mix in TryPeek + TryGet on every iteration — both
                    // paths should agree.
                    if (!JustCache.TryPeek(key, buf, out var peekLen))
                    {
                        errors.Add($"peek miss tid={tid} i={i}");
                        continue;
                    }
                    if (!JustCache.TryGet(key, buf, out var getLen))
                    {
                        errors.Add($"get miss tid={tid} i={i}");
                        continue;
                    }
                    if (peekLen != val.Length || getLen != val.Length)
                    {
                        errors.Add($"len mismatch tid={tid} i={i} peek={peekLen} get={getLen} expect={val.Length}");
                    }
                }
            }) { IsBackground = true };
            workers[t].Start();
        }
        foreach (var w in workers) w.Join();

        Assert.Empty(errors);

        // Spot-check a few random entries from each thread.
        var verifyBuf = new byte[32];
        for (var t = 0; t < threads; t++)
        {
            for (var i = 0; i < opsPerThread; i += 250)
            {
                var key = Encoding.UTF8.GetBytes($"conc:t{t}:k{i}");
                Assert.True(JustCache.TryPeek(key, verifyBuf, out var n));
                Assert.Equal($"v-{t}-{i}", Encoding.UTF8.GetString(verifyBuf, 0, n));
            }
        }
    }

    [Fact]
    public void Concurrent_PerKey_LastWriterWins()
    {
        // 8 threads all clobber the *same* key. The cache must end with
        // exactly one of their values (not a mix) and survive without
        // crashing. This is the classic "lock correctness" smoke test
        // for a sharded structure — all 8 threads share one shard.
        const int threads = 8;
        const int opsPerThread = 2_000;

        var key = Encoding.UTF8.GetBytes("conc:shared:key");
        JustCache.ClearAll();

        var workers = new Thread[threads];
        for (var t = 0; t < threads; t++)
        {
            var tid = t;
            workers[t] = new Thread(() =>
            {
                for (var i = 0; i < opsPerThread; i++)
                {
                    var val = Encoding.UTF8.GetBytes($"writer-{tid}");
                    JustCache.Set(key, val);
                }
            }) { IsBackground = true };
            workers[t].Start();
        }
        foreach (var w in workers) w.Join();

        var buf = new byte[64];
        Assert.True(JustCache.TryGet(key, buf, out var n));
        var winner = Encoding.UTF8.GetString(buf, 0, n);
        Assert.Matches("^writer-[0-7]$", winner);
    }
}
