using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace LiteAPI.Cache.Benchmarks;

[MemoryDiagnoser]
public class KvBenchmarks
{
    public IEnumerable<CacheBackend> Backends =>
        RedisBenchClient.IsAvailable()
            ? new[] { CacheBackend.MemoryCache, CacheBackend.JustCache, CacheBackend.Redis }
            : new[] { CacheBackend.MemoryCache, CacheBackend.JustCache };

    [ParamsSource(nameof(Backends))]
    public CacheBackend Backend { get; set; }

    [Params(32, 1024, 32 * 1024)]
    public int PayloadBytes { get; set; }

    private byte[] _value = null!;
    private string _prefix = null!;

    private string _hitKey = null!;
    private string _missKey = null!;
    private string _setKey = null!;
    private string _removeKey = null!;

    private byte[]? _hitKeyBytes;
    private byte[]? _missKeyBytes;
    private byte[]? _setKeyBytes;
    private byte[]? _removeKeyBytes;
    private byte[]? _justCacheGetBuffer;

    private MemoryCache? _memory;
    private RedisBenchClient? _redis;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _value = TestData.CreatePayload(PayloadBytes);
        _prefix = $"bench:{Guid.NewGuid():N}:";

        _hitKey = _prefix + "hit";
        _missKey = _prefix + "miss";
        _setKey = _prefix + "set";
        _removeKey = _prefix + "remove";

        switch (Backend)
        {
            case CacheBackend.MemoryCache:
                _memory = new MemoryCache(new MemoryCacheOptions());
                _memory.Set(_hitKey, _value);
                break;

            case CacheBackend.JustCache:
                JustCacheBootstrap.EnsureInitialized();
                LiteAPI.Cache.JustCache.ClearAll();
                _hitKeyBytes = Encoding.UTF8.GetBytes(_hitKey);
                _missKeyBytes = Encoding.UTF8.GetBytes(_missKey);
                _setKeyBytes = Encoding.UTF8.GetBytes(_setKey);
                _removeKeyBytes = Encoding.UTF8.GetBytes(_removeKey);
                _justCacheGetBuffer = new byte[PayloadBytes];
                LiteAPI.Cache.JustCache.Set(_hitKeyBytes, _value);
                break;

            case CacheBackend.Redis:
                _redis = RedisBenchClient.ConnectOrThrow();
                _redis.Db.StringSet(_hitKey, _value);
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
    public void Set_Overwrite()
    {
        switch (Backend)
        {
            case CacheBackend.MemoryCache:
                _memory!.Set(_setKey, _value);
                break;
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.Set(_setKeyBytes!, _value);
                break;
            case CacheBackend.Redis:
                _redis!.Db.StringSet(_setKey, _value);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Benchmark]
    public int Get_Hit()
    {
        switch (Backend)
        {
            case CacheBackend.MemoryCache:
                return ((_memory!.Get(_hitKey) as byte[])?.Length) ?? -1;
            case CacheBackend.JustCache:
            {
                var ok = LiteAPI.Cache.JustCache.TryGet(_hitKeyBytes!, _justCacheGetBuffer!, out var written);
                return ok ? written : -1;
            }
            case CacheBackend.Redis:
            {
                var v = _redis!.Db.StringGet(_hitKey);
                return v.HasValue ? (int)v.Length() : -1;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Benchmark]
    public int Get_Miss()
    {
        switch (Backend)
        {
            case CacheBackend.MemoryCache:
                return _memory!.TryGetValue(_missKey, out _) ? 1 : 0;
            case CacheBackend.JustCache:
            {
                var ok = LiteAPI.Cache.JustCache.TryGet(_missKeyBytes!, _justCacheGetBuffer!, out _);
                return ok ? 1 : 0;
            }
            case CacheBackend.Redis:
                return _redis!.Db.StringGet(_missKey).HasValue ? 1 : 0;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [IterationSetup(Target = nameof(Remove_Existing))]
    public void IterationSetupRemove()
    {
        switch (Backend)
        {
            case CacheBackend.MemoryCache:
                _memory!.Set(_removeKey, _value);
                break;
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.Set(_removeKeyBytes!, _value);
                break;
            case CacheBackend.Redis:
                _redis!.Db.StringSet(_removeKey, _value);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Benchmark]
    public void Remove_Existing()
    {
        switch (Backend)
        {
            case CacheBackend.MemoryCache:
                _memory!.Remove(_removeKey);
                break;
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.Remove(_removeKeyBytes!);
                break;
            case CacheBackend.Redis:
                _redis!.Db.KeyDelete(_removeKey);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
