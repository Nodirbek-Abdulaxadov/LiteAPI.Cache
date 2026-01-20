using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;

namespace LiteAPI.Cache.Benchmarks;

[MemoryDiagnoser]
public class TtlSetBenchmarks
{
    public IEnumerable<CacheBackend> Backends =>
        RedisBenchClient.IsAvailable()
            ? new[] { CacheBackend.MemoryCache, CacheBackend.JustCache, CacheBackend.Redis }
            : new[] { CacheBackend.MemoryCache, CacheBackend.JustCache };

    [ParamsSource(nameof(Backends))]
    public CacheBackend Backend { get; set; }

    [Params(32, 1024, 32 * 1024)]
    public int PayloadBytes { get; set; }

    [Params(1000)]
    public int TtlMs { get; set; }

    private byte[] _value = null!;
    private string _key = null!;

    private MemoryCache? _memory;
    private RedisBenchClient? _redis;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _value = TestData.CreatePayload(PayloadBytes);
        _key = $"bench:{Guid.NewGuid():N}:ttl";

        switch (Backend)
        {
            case CacheBackend.MemoryCache:
                _memory = new MemoryCache(new MemoryCacheOptions());
                break;

            case CacheBackend.JustCache:
                JustCacheBootstrap.EnsureInitialized();
                LiteAPI.Cache.JustCache.ClearAll();
                break;

            case CacheBackend.Redis:
                _redis = RedisBenchClient.ConnectOrThrow();
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _memory?.Dispose();
        _redis?.Dispose();
    }

    [Benchmark]
    public void Set_WithTtl()
    {
        var ttl = TimeSpan.FromMilliseconds(TtlMs);

        switch (Backend)
        {
            case CacheBackend.MemoryCache:
                _memory!.Set(_key, _value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
                break;
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.SetWithTtl(_key, _value, ttl);
                break;
            case CacheBackend.Redis:
                _redis!.Db.StringSet(_key, _value, ttl);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

[MemoryDiagnoser]
public class ExpireBenchmarks
{
    public IEnumerable<CacheBackend> Backends =>
        RedisBenchClient.IsAvailable()
            ? new[] { CacheBackend.JustCache, CacheBackend.Redis }
            : new[] { CacheBackend.JustCache };

    [ParamsSource(nameof(Backends))]
    public CacheBackend Backend { get; set; }

    [Params(1000)]
    public int TtlMs { get; set; }

    private byte[] _value = null!;
    private string _key = null!;

    private RedisBenchClient? _redis;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _value = TestData.CreatePayload(64);
        _key = $"bench:{Guid.NewGuid():N}:expire";

        switch (Backend)
        {
            case CacheBackend.JustCache:
                JustCacheBootstrap.EnsureInitialized();
                LiteAPI.Cache.JustCache.ClearAll();
                break;

            case CacheBackend.Redis:
                _redis = RedisBenchClient.ConnectOrThrow();
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _redis?.Dispose();
    }

    [IterationSetup(Target = nameof(Expire_Existing))]
    public void IterationSetup()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.Set(_key, _value);
                break;
            case CacheBackend.Redis:
                _redis!.Db.StringSet(_key, _value);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Benchmark]
    public bool Expire_Existing()
    {
        var ttl = TimeSpan.FromMilliseconds(TtlMs);

        return Backend switch
        {
            CacheBackend.JustCache => LiteAPI.Cache.JustCache.Expire(_key, ttl),
            CacheBackend.Redis => _redis!.Db.KeyExpire(_key, ttl),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

[MemoryDiagnoser]
public class TtlQueryBenchmarks
{
    public IEnumerable<CacheBackend> Backends =>
        RedisBenchClient.IsAvailable()
            ? new[] { CacheBackend.JustCache, CacheBackend.Redis }
            : new[] { CacheBackend.JustCache };

    [ParamsSource(nameof(Backends))]
    public CacheBackend Backend { get; set; }

    private string _key = null!;
    private RedisBenchClient? _redis;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _key = $"bench:{Guid.NewGuid():N}:ttlq";

        switch (Backend)
        {
            case CacheBackend.JustCache:
                JustCacheBootstrap.EnsureInitialized();
                LiteAPI.Cache.JustCache.ClearAll();
                LiteAPI.Cache.JustCache.SetStringWithTtl(_key, "v", TimeSpan.FromSeconds(10));
                break;

            case CacheBackend.Redis:
                _redis = RedisBenchClient.ConnectOrThrow();
                _redis.Db.StringSet(_key, "v", TimeSpan.FromSeconds(10));
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _redis?.Dispose();
    }

    [Benchmark]
    public long Ttl_Query()
    {
        if (Backend == CacheBackend.JustCache)
            return LiteAPI.Cache.JustCache.TtlMs(_key);

        if (Backend == CacheBackend.Redis)
        {
            var ttl = _redis!.Db.KeyTimeToLive(_key);
            return ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : -2;
        }

        throw new ArgumentOutOfRangeException();
    }
}
