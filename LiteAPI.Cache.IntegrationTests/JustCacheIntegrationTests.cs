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
        JustCache.SetMaxItems(2);

        JustCache.SetString("lru:k1", "1");
        JustCache.SetString("lru:k2", "2");

        _ = JustCache.GetString("lru:k1");

        JustCache.SetString("lru:k3", "3");

        Assert.NotNull(JustCache.GetString("lru:k1"));
        Assert.Null(JustCache.GetString("lru:k2"));
        Assert.NotNull(JustCache.GetString("lru:k3"));
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

        JustCache.SetMaxItems(1);
        JustCache.SetString("notify:k1", "1");
        JustCache.SetString("notify:k2", "2");

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
}
