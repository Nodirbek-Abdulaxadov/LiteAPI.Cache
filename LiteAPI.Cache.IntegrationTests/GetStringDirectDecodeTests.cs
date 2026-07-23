using System.Text;
using LiteAPI.Cache;
using Xunit;

namespace LiteAPI.Cache.IntegrationTests;

[Collection("JustCacheCollection")]
public sealed class GetStringDirectDecodeTests
{
    private readonly JustCacheFixture _fixture;

    public GetStringDirectDecodeTests(JustCacheFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Theory]
    [InlineData("plain-ascii")]
    [InlineData("salom, дунё! 🌍 café ☕ — 日本語")]
    [InlineData("")] // empty stored value reads back as null (unchanged semantics)
    public void GetString_RoundTrips(string value)
    {
        const string key = "gs:roundtrip";
        JustCache.SetString(key, value);

        var read = JustCache.GetString(key);

        if (value.Length == 0)
            Assert.Null(read); // empty value == miss, preserved from the byte[] path
        else
            Assert.Equal(value, read);
    }

    [Fact]
    public void GetString_Miss_ReturnsNull()
    {
        Assert.Null(JustCache.GetString("gs:absent-key"));
    }

    [Fact]
    public void GetString_AllocatesLessThan_GetPlusManualDecode()
    {
        const string key = "gs:alloc:key";
        var value = new string('x', 512);
        JustCache.SetString(key, value);

        // Warm up JIT / lazy init for both paths.
        _ = JustCache.GetString(key);
        _ = Encoding.UTF8.GetString(JustCache.Get(key)!);

        const int iters = 5_000;

        var b0 = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iters; i++)
            _ = JustCache.GetString(key);
        var directDecode = GC.GetAllocatedBytesForCurrentThread() - b0;

        var b1 = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iters; i++)
            _ = Encoding.UTF8.GetString(JustCache.Get(key)!);
        var viaIntermediateArray = GC.GetAllocatedBytesForCurrentThread() - b1;

        // Direct decode skips the intermediate byte[] the manual path must
        // allocate, so it allocates strictly less over the same iterations.
        Assert.True(
            directDecode < viaIntermediateArray,
            $"GetString direct={directDecode} B should be < Get+decode={viaIntermediateArray} B over {iters} iters");
    }
}
