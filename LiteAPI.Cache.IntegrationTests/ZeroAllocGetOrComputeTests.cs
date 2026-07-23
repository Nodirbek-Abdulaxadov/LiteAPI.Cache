using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteAPI.Cache;
using Xunit;

namespace LiteAPI.Cache.IntegrationTests;

[Collection("JustCacheCollection")]
public sealed class ZeroAllocGetOrComputeTests
{
    private readonly JustCacheFixture _fixture;

    public ZeroAllocGetOrComputeTests(JustCacheFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public void GetOrCompute_ByteKeySpan_ZeroAllocationOnHit()
    {
        var key = Encoding.UTF8.GetBytes("goc:zeroalloc:hit");
        var value = Encoding.UTF8.GetBytes(new string('v', 256));
        JustCache.Set(key, value);

        var buffer = new byte[value.Length];
        Func<byte[]> boom = () => throw new InvalidOperationException("factory must not run on a hit");

        // Warm up JIT / lazy init.
        Assert.True(JustCache.GetOrCompute(key, buffer, boom, out var written));
        Assert.Equal(value.Length, written);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            if (!JustCache.GetOrCompute(key, buffer, boom, out written))
                throw new InvalidOperationException("expected hit");
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void GetOrCompute_ByteKeySpan_BufferTooSmall_ReturnsRequiredLength()
    {
        var key = Encoding.UTF8.GetBytes("goc:zeroalloc:small");
        var value = Encoding.UTF8.GetBytes("0123456789"); // 10 bytes
        JustCache.Set(key, value);

        var tiny = new byte[4];
        Func<byte[]> boom = () => throw new InvalidOperationException("value exists; factory must not run");

        var ok = JustCache.GetOrCompute(key, tiny, boom, out var written);

        Assert.False(ok);
        Assert.Equal(value.Length, written); // required length, not bytes copied
    }

    [Fact]
    public void GetOrCompute_ByteKeySpan_Miss_ComputesCachesAndCopies()
    {
        var key = Encoding.UTF8.GetBytes("goc:zeroalloc:miss");
        var value = Encoding.UTF8.GetBytes("computed-value");
        var buffer = new byte[64];

        var ok = JustCache.GetOrCompute(key, buffer, () => value, out var written);

        Assert.True(ok);
        Assert.Equal(value.Length, written);
        Assert.Equal(value, buffer.AsSpan(0, written).ToArray());
        Assert.Equal(value, JustCache.Get(key)); // and it is now cached
    }

    [Fact]
    public async Task GetOrCompute_ByteKeySpan_ConcurrentMisses_RunsFactoryOnce()
    {
        var key = Encoding.UTF8.GetBytes("goc:zeroalloc:stampede");
        var value = Encoding.UTF8.GetBytes("the-once-computed-value");
        const int workers = 256;

        var calls = 0;
        using var start = new ManualResetEventSlim(false);
        var tasks = new Task[workers];

        for (var i = 0; i < workers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                start.Wait();
                var buffer = new byte[64];
                var ok = JustCache.GetOrCompute(key, buffer, () =>
                {
                    Interlocked.Increment(ref calls);
                    Thread.Sleep(50);
                    return value;
                }, out var written);
                Assert.True(ok);
                Assert.Equal(value, buffer.AsSpan(0, written).ToArray());
            });
        }

        start.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task GetOrComputeAsync_Hit_CompletesSynchronously_NoTaskAllocation()
    {
        const string key = "goc:vt:hit";
        JustCache.SetString(key, "cached");

        Func<string, CancellationToken, Task<byte[]>> boom =
            (_, _) => throw new InvalidOperationException("factory must not run on a hit");

        var vt = JustCache.GetOrComputeAsync(key, boom);

        // A hit is a synchronously-completed ValueTask — no Task was allocated.
        Assert.True(vt.IsCompletedSuccessfully);
        var result = await vt; // already completed; consumes the ValueTask once
        Assert.Equal(Encoding.UTF8.GetBytes("cached"), result);
    }
}
