using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteAPI.Cache;
using Xunit;

namespace LiteAPI.Cache.IntegrationTests;

[Collection("JustCacheCollection")]
public sealed class StampedeTests
{
    private readonly JustCacheFixture _fixture;

    public StampedeTests(JustCacheFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task GetOrCompute_ConcurrentMissesOfOneColdKey_RunsFactoryOnce()
    {
        const string key = "stampede:sync:cold-key";
        const int workers = 256;
        var payload = Encoding.UTF8.GetBytes("the-expensive-value");

        var calls = 0;
        using var start = new ManualResetEventSlim(false);
        var tasks = new Task<byte[]>[workers];

        for (var i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                start.Wait();
                return JustCache.GetOrCompute(key, _ =>
                {
                    Interlocked.Increment(ref calls);
                    // Widen the miss window so every worker overlaps in-flight.
                    Thread.Sleep(50);
                    return payload;
                });
            });
        }

        start.Set();
        var results = await Task.WhenAll(tasks);

        // The whole point: 256 concurrent misses -> exactly one factory run.
        Assert.Equal(1, Volatile.Read(ref calls));
        foreach (var r in results)
            Assert.Equal(payload, r);

        // And the value is now cached.
        Assert.Equal(payload, JustCache.Get(key));
    }

    [Fact]
    public async Task GetOrComputeAsync_ConcurrentMissesOfOneColdKey_RunsFactoryOnce()
    {
        const string key = "stampede:async:cold-key";
        const int workers = 256;
        var payload = Encoding.UTF8.GetBytes("the-expensive-async-value");

        var calls = 0;
        using var start = new ManualResetEventSlim(false);

        Func<string, CancellationToken, Task<byte[]>> factory = async (_, ct) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50, ct);
            return payload;
        };

        var tasks = new Task<byte[]>[workers];
        for (var i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                start.Wait();
                return await JustCache.GetOrComputeAsync(key, factory);
            });
        }

        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, Volatile.Read(ref calls));
        foreach (var r in results)
            Assert.Equal(payload, r);

        Assert.Equal(payload, JustCache.Get(key));
    }

    [Fact]
    public void GetOrCompute_OnHit_DoesNotCallFactory()
    {
        const string key = "stampede:hit:preset";
        var payload = Encoding.UTF8.GetBytes("already-here");
        JustCache.Set(key, payload);

        var result = JustCache.GetOrCompute(key, _ =>
            throw new InvalidOperationException("factory must not run on a cache hit"));

        Assert.Equal(payload, result);
    }

    [Fact]
    public void GetOrCompute_FactoryThrows_DoesNotPoisonLaterCalls()
    {
        const string key = "stampede:retry:after-throw";
        var payload = Encoding.UTF8.GetBytes("recovered");

        Assert.Throws<InvalidOperationException>(() =>
            JustCache.GetOrCompute(key, _ => throw new InvalidOperationException("boom")));

        // A thrown factory retires the in-flight slot, so the next call retries
        // rather than replaying the cached exception.
        var result = JustCache.GetOrCompute(key, _ => payload);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void GetOrCompute_WithTtl_StoresWithExpiry()
    {
        const string key = "stampede:ttl:key";
        var payload = Encoding.UTF8.GetBytes("expires-soon");

        var result = JustCache.GetOrCompute(key, _ => payload, TimeSpan.FromSeconds(30));
        Assert.Equal(payload, result);

        var ttl = JustCache.TtlMs(key);
        Assert.InRange(ttl, 1, 30_000);
    }

    [Fact]
    public async Task GetOrComputeString_ConcurrentMisses_RunsFactoryOnce()
    {
        const string key = "stampede:string:cold-key";
        const int workers = 128;
        const string value = "computed-string-value";

        var calls = 0;
        using var start = new ManualResetEventSlim(false);
        var tasks = new Task<string>[workers];

        for (var i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                start.Wait();
                return JustCache.GetOrComputeString(key, _ =>
                {
                    Interlocked.Increment(ref calls);
                    Thread.Sleep(50);
                    return value;
                });
            });
        }

        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, Volatile.Read(ref calls));
        foreach (var r in results)
            Assert.Equal(value, r);
        Assert.Equal(value, JustCache.GetString(key));
    }
}
