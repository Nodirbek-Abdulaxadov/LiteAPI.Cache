using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteAPI.Cache;
using Xunit;

namespace LiteAPI.Cache.IntegrationTests;

[Collection("JustCacheCollection")]
public sealed class ConcurrentReadTests
{
    private readonly JustCacheFixture _fixture;

    public ConcurrentReadTests(JustCacheFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // Exercises the sampled-LRU read path (read lock + occasional write-lock
    // recency bump) under concurrency: every read must still return the exact
    // stored value, regardless of promotion races.
    [Fact]
    public async Task TryGet_ConcurrentReaders_AlwaysReturnCorrectValue()
    {
        const int k = 64;
        var keys = new byte[k][];
        var vals = new byte[k][];
        for (var i = 0; i < k; i++)
        {
            keys[i] = Encoding.UTF8.GetBytes($"cr:key:{i}");
            vals[i] = Encoding.UTF8.GetBytes($"cr-value-{i}-" + new string((char)('a' + (i % 26)), 40));
            JustCache.Set(keys[i], vals[i]);
        }

        var workers = System.Math.Max(4, System.Environment.ProcessorCount);
        var mismatches = 0;
        var tasks = new Task[workers];
        for (var w = 0; w < workers; w++)
        {
            tasks[w] = Task.Run(() =>
            {
                var buf = new byte[256];
                for (var it = 0; it < 150_000; it++)
                {
                    var i = it % k;
                    if (!JustCache.TryGet(keys[i], buf, out var n) ||
                        !buf.AsSpan(0, n).SequenceEqual(vals[i]))
                    {
                        Interlocked.Exchange(ref mismatches, 1);
                        return;
                    }
                }
            });
        }

        await Task.WhenAll(tasks);
        Assert.Equal(0, Volatile.Read(ref mismatches));
    }
}
