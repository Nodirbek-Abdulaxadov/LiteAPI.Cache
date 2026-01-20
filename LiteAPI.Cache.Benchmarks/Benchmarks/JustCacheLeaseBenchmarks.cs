using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace LiteAPI.Cache.Benchmarks;

[MemoryDiagnoser]
public class JustCacheLeaseBenchmarks
{
    [Params(32, 1024, 32 * 1024)]
    public int PayloadBytes { get; set; }

    private byte[] _value = null!;

    private string _keyStr = null!;
    private byte[] _keyBytes = null!;

    private byte[] _buffer = null!;
    private MemoryCache _memory = null!;
    private RedisBenchClient _redis = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _value = TestData.CreatePayload(PayloadBytes);
        _keyStr = $"lease:bench:{Guid.NewGuid():N}";
        _keyBytes = Encoding.UTF8.GetBytes(_keyStr);
        _buffer = new byte[PayloadBytes];

        _memory = new MemoryCache(new MemoryCacheOptions());
        _memory.Set(_keyStr, _value);

        JustCacheBootstrap.EnsureInitialized();
        LiteAPI.Cache.JustCache.ClearAll();

        LiteAPI.Cache.JustCache.Set(_keyBytes, _value);

        _redis = RedisBenchClient.ConnectOrThrow();
        _redis.Db.StringSet(_keyStr, _value);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _memory.Dispose();
        _redis.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int Get_Hit_MemoryCache()
    {
        return ((_memory.Get(_keyStr) as byte[])?.Length) ?? -1;
    }

    [Benchmark]
    public int Get_Hit_CopyInto()
    {
        var ok = LiteAPI.Cache.JustCache.TryGet(_keyBytes, _buffer, out var written);
        return ok ? written : -1;
    }

    [Benchmark]
    public int Get_Hit_Lease()
    {
        using var lease = LiteAPI.Cache.JustCache.GetLease(_keyBytes);
        var span = lease.Span;
        return lease.IsValid ? span.Length : -1;
    }

    [Benchmark]
    public int Get_Hit_Redis()
    {
        var v = _redis.Db.StringGet(_keyStr);
        return v.HasValue ? (int)v.Length() : -1;
    }
}
