using System.Text;
using LiteAPI.Cache;
using Xunit;

namespace LiteAPI.Cache.IntegrationTests;

public sealed class GcFreeHotPathTests
{
    [Fact]
    public void TryGet_WithCallerBuffer_DoesNotAllocate_OnHitPath()
    {
        JustCache.Initialize();
        JustCache.ClearAll();

        var key = Encoding.UTF8.GetBytes("gcfree:test:key");
        var payload = new byte[32 * 1024];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i & 0xFF);

        JustCache.Set(key, payload);

        var buffer = new byte[payload.Length];

        // Warm up JIT and any lazy init.
        Assert.True(JustCache.TryGet(key, buffer, out var written));
        Assert.Equal(payload.Length, written);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            if (!JustCache.TryGet(key, buffer, out written))
                throw new InvalidOperationException("Expected hit");
            if (written != payload.Length)
                throw new InvalidOperationException("Unexpected length");
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
